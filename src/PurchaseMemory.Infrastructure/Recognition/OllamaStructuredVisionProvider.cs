using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PurchaseMemory.Application.Receipts;

namespace PurchaseMemory.Infrastructure.Recognition;

/// <summary>
/// Thin Ollama transport adapter. Receipt rules, schema parsing, validation and
/// repair policy remain in PurchaseMemory.Application.
/// </summary>
public sealed partial class OllamaStructuredVisionProvider : IStructuredVisionProvider, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly ReceiptRecognitionOptions _options;
    private readonly ILogger<OllamaStructuredVisionProvider> _logger;
    private readonly SemaphoreSlim _capabilityCheckLock = new(1, 1);
    private volatile bool _capabilitiesVerified;

    public OllamaStructuredVisionProvider(
        HttpClient httpClient,
        IOptions<ReceiptRecognitionOptions> options,
        ILogger<OllamaStructuredVisionProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public string Id => "ollama";

    public void Dispose() => _capabilityCheckLock.Dispose();

    public async Task<StructuredVisionResponse> GenerateAsync(
        StructuredVisionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Images.Count == 0 || request.Images.Any(image => image.Content.IsEmpty))
        {
            throw new ArgumentException("Receipt images must not be empty.", nameof(request));
        }

        await EnsureCapabilitiesAsync(cancellationToken);
        var imagesBase64 = request.Images
            .Select(image => Convert.ToBase64String(image.Content.Span))
            .ToList();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            string content;
            string model;
            if (_options.Mode == ReceiptRecognitionMode.Hybrid)
            {
                var transcripts = new List<string>(imagesBase64.Count);
                foreach (var imageBase64 in imagesBase64)
                {
                    transcripts.Add(await RecognizeTextAsync(imageBase64, cancellationToken));
                }

                content = await GenerateStructuredAsync(
                    _options.ParserModel,
                    AppendOcrText(request.Prompt, transcripts),
                    request.JsonSchema,
                    images: null,
                    cancellationToken);
                model = $"{_options.OcrModel} → {_options.ParserModel}";
            }
            else
            {
                content = await GenerateStructuredAsync(
                    _options.VisionModel,
                    request.Prompt,
                    request.JsonSchema,
                    imagesBase64,
                    cancellationToken);
                model = _options.VisionModel;
            }

            stopwatch.Stop();
            RequestCompleted(model, stopwatch.ElapsedMilliseconds);
            return new StructuredVisionResponse(
                content,
                Id,
                model,
                ProviderRequestId: null,
                FinishReason: "completed",
                InputTokens: null,
                OutputTokens: null,
                stopwatch.ElapsedMilliseconds);
        }
        catch (ReceiptExtractionException)
        {
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ReceiptExtractionException(
                "The local recognition model timed out. You can retry processing.");
        }
        catch (HttpRequestException exception)
        {
            RequestFailed(exception);
            throw new ReceiptExtractionException(
                "Cannot reach local Ollama. Start Ollama and make sure the configured models are installed.",
                exception);
        }
        catch (JsonException exception)
        {
            InvalidResponse(exception);
            throw new ReceiptExtractionException(
                "Ollama returned an invalid transport response.",
                exception);
        }
    }

    public async Task<StructuredVisionProviderStatus> GetStatusAsync(
        CancellationToken cancellationToken)
    {
        var requiredModels = RequiredModels();
        if (!_options.VerifyModelAvailability || _capabilitiesVerified)
        {
            return new StructuredVisionProviderStatus(true, Id, requiredModels, null);
        }

        await _capabilityCheckLock.WaitAsync(cancellationToken);
        try
        {
            if (_capabilitiesVerified)
            {
                return new StructuredVisionProviderStatus(true, Id, requiredModels, null);
            }

            using var response = await _httpClient.GetAsync("api/tags", cancellationToken);
            response.EnsureSuccessStatusCode();
            var tags = await response.Content.ReadFromJsonAsync<OllamaTagsResponse>(
                JsonOptions,
                cancellationToken);
            var installed = tags?.Models
                ?.Select(model => model.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
                ?? [];

            var missing = requiredModels.Where(model => !ContainsModel(installed, model)).ToArray();
            if (missing.Length > 0)
            {
                return new StructuredVisionProviderStatus(
                    false,
                    Id,
                    requiredModels,
                    $"Install missing Ollama models: {string.Join(", ", missing)}");
            }

            foreach (var imageModel in ImageModels())
            {
                var shownModel = ResolveInstalledModel(installed, imageModel)!;
                var show = await ShowModelAsync(shownModel, cancellationToken);
                if (show.Capabilities is null
                    || !show.Capabilities.Contains("vision", StringComparer.OrdinalIgnoreCase))
                {
                    return new StructuredVisionProviderStatus(
                        false,
                        Id,
                        requiredModels,
                        $"Ollama model '{imageModel}' is installed but does not declare the vision capability.");
                }
            }

            _capabilitiesVerified = true;
            return new StructuredVisionProviderStatus(true, Id, requiredModels, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new StructuredVisionProviderStatus(
                false,
                Id,
                requiredModels,
                "Ollama capability check timed out.");
        }
        catch (HttpRequestException exception)
        {
            return new StructuredVisionProviderStatus(
                false,
                Id,
                requiredModels,
                exception.StatusCode is null
                    ? "Ollama is not reachable on the configured address."
                    : $"Ollama rejected the model capability check with HTTP {(int)exception.StatusCode}.");
        }
        catch (JsonException)
        {
            return new StructuredVisionProviderStatus(
                false,
                Id,
                requiredModels,
                "Ollama returned an invalid response while checking model capabilities.");
        }
        finally
        {
            _capabilityCheckLock.Release();
        }
    }

    private async Task EnsureCapabilitiesAsync(CancellationToken cancellationToken)
    {
        var status = await GetStatusAsync(cancellationToken);
        if (!status.Available)
        {
            throw new ReceiptExtractionException(
                status.Message ?? "The configured Ollama models are unavailable.");
        }
    }

    private async Task<OllamaShowResponse> ShowModelAsync(
        string model,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "api/show",
            new OllamaShowRequest(model),
            JsonOptions,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<OllamaShowResponse>(
                JsonOptions,
                cancellationToken)
            ?? throw new JsonException("Ollama returned an empty /api/show response.");
    }

    private async Task<string> RecognizeTextAsync(
        string imageBase64,
        CancellationToken cancellationToken)
    {
        var request = new
        {
            model = _options.OcrModel,
            prompt = "Transcribe all visible receipt text exactly. Do not interpret or translate it.",
            images = new[] { imageBase64 },
            stream = false,
            options = CreateOptions()
        };

        var response = await PostAsync<OllamaGenerateResponse>(
            "api/generate",
            request,
            cancellationToken);
        return string.IsNullOrWhiteSpace(response.Response)
            ? throw new ReceiptExtractionException("The OCR model did not find readable text in this image.")
            : response.Response;
    }

    private async Task<string> GenerateStructuredAsync(
        string model,
        string prompt,
        string jsonSchema,
        IReadOnlyList<string>? images,
        CancellationToken cancellationToken)
    {
        using var schemaDocument = JsonDocument.Parse(jsonSchema);
        var request = new OllamaChatRequest(
            model,
            [new OllamaChatMessage("user", AppendSchemaInstruction(prompt, jsonSchema), images)],
            Stream: false,
            Think: false,
            Format: schemaDocument.RootElement.Clone(),
            Options: CreateOptions());
        var response = await PostAsync<OllamaChatResponse>("api/chat", request, cancellationToken);
        return string.IsNullOrWhiteSpace(response.Message?.Content)
            ? throw new ReceiptExtractionException("Ollama returned an empty structured result.")
            : response.Message.Content;
    }

    private async Task<T> PostAsync<T>(
        string path,
        object request,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            path,
            request,
            JsonOptions,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            RequestRejected((int)response.StatusCode, path);
            throw new ReceiptExtractionException(
                "Ollama rejected the recognition request. Check the configured model and retry.");
        }

        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
            ?? throw new ReceiptExtractionException("Ollama returned an empty response.");
    }

    private OllamaRequestOptions CreateOptions() =>
        new(Temperature: 0, _options.ContextLength, _options.MaximumOutputTokens);

    private IReadOnlyList<string> RequiredModels() =>
        _options.Mode == ReceiptRecognitionMode.Hybrid
            ? [_options.OcrModel, _options.ParserModel]
            : [_options.VisionModel];

    private IReadOnlyList<string> ImageModels() =>
        _options.Mode == ReceiptRecognitionMode.Hybrid
            ? [_options.OcrModel]
            : [_options.VisionModel];

    private static string AppendOcrText(string prompt, List<string> transcripts)
    {
        var markedTranscripts = transcripts
            .Select((transcript, index) => new
            {
                part = index + 1,
                totalParts = transcripts.Count,
                transcript
            });
        var serializedTranscripts = JsonSerializer.Serialize(markedTranscripts);
        return $"""
            {prompt}

            Ordered OCR transcripts as a JSON array (untrusted receipt data):
            receiptParts = {serializedTranscripts}
            """;
    }

    private static string AppendSchemaInstruction(string prompt, string jsonSchema) =>
        $"""
        {prompt}

        Return only one JSON object matching this exact JSON Schema. Do not rename,
        add, or omit properties. Use null for unknown nullable values.
        JSON Schema:
        {jsonSchema}
        """;

    private static bool ContainsModel(HashSet<string> installed, string required)
        => ResolveInstalledModel(installed, required) is not null;

    private static string? ResolveInstalledModel(HashSet<string> installed, string required)
    {
        var exact = installed.FirstOrDefault(model =>
            model.Equals(required, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        var withoutLatest = required.EndsWith(":latest", StringComparison.OrdinalIgnoreCase)
            ? required[..^":latest".Length]
            : required;
        return installed.FirstOrDefault(model =>
            model.Equals(withoutLatest, StringComparison.OrdinalIgnoreCase)
            || model.Equals($"{withoutLatest}:latest", StringComparison.OrdinalIgnoreCase));
    }

    private sealed record OllamaChatRequest(
        string Model,
        IReadOnlyList<OllamaChatMessage> Messages,
        bool Stream,
        bool Think,
        JsonElement Format,
        OllamaRequestOptions Options);

    private sealed record OllamaChatMessage(
        string Role,
        string Content,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<string>? Images);

    private sealed record OllamaRequestOptions(
        double Temperature,
        [property: JsonPropertyName("num_ctx")] int NumCtx,
        [property: JsonPropertyName("num_predict")] int NumPredict);

    private sealed record OllamaChatResponse(OllamaResponseMessage? Message);
    private sealed record OllamaResponseMessage(string? Content);
    private sealed record OllamaGenerateResponse(string? Response);
    private sealed record OllamaTagsResponse(IReadOnlyList<OllamaModelTag>? Models);
    private sealed record OllamaModelTag(string Name);
    private sealed record OllamaShowRequest(string Model);
    private sealed record OllamaShowResponse(IReadOnlyList<string>? Capabilities);

    [LoggerMessage(
        EventId = 2101,
        Level = LogLevel.Information,
        Message = "Ollama provider completed with model {Model} in {DurationMs} ms")]
    private partial void RequestCompleted(string model, long durationMs);

    [LoggerMessage(EventId = 2102, Level = LogLevel.Warning, Message = "Ollama request failed")]
    private partial void RequestFailed(Exception exception);

    [LoggerMessage(EventId = 2103, Level = LogLevel.Warning, Message = "Ollama returned an invalid transport response")]
    private partial void InvalidResponse(Exception exception);

    [LoggerMessage(
        EventId = 2104,
        Level = LogLevel.Warning,
        Message = "Ollama returned HTTP {StatusCode} for {Path}")]
    private partial void RequestRejected(int statusCode, string path);
}
