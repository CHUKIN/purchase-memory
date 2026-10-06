using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PurchaseMemory.Application.Receipts;

namespace PurchaseMemory.Infrastructure.Recognition;

/// <summary>
/// Thin Ollama transport adapter for text-only structured completions.
///
/// The model tag comes from configuration (<see cref="ReceiptRecognitionOptions.RowsParserModel"/>)
/// and is never hard-coded: Ollama proxies its cloud models through the same
/// local daemon, so switching between a local and a cloud parser has to be a
/// configuration change rather than a code change.
///
/// Unlike <see cref="OllamaStructuredVisionProvider"/> this provider does not
/// require the model to declare the vision capability — a text parser has none.
/// </summary>
public sealed partial class OllamaStructuredTextProvider : IStructuredTextProvider, IDisposable
{
    public const string ProviderId = "ollama-text";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly ReceiptRecognitionOptions _options;
    private readonly ILogger<OllamaStructuredTextProvider> _logger;
    private readonly SemaphoreSlim _capabilityCheckLock = new(1, 1);
    private volatile string? _verifiedModel;

    public OllamaStructuredTextProvider(
        HttpClient httpClient,
        IOptions<ReceiptRecognitionOptions> options,
        ILogger<OllamaStructuredTextProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public string Id => ProviderId;

    public void Dispose() => _capabilityCheckLock.Dispose();

    public async Task<StructuredTextResponse> GenerateAsync(
        StructuredTextRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Prompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.JsonSchema);

        var model = RequireModel();
        await EnsureModelInstalledAsync(cancellationToken);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var schemaDocument = JsonDocument.Parse(request.JsonSchema);
            var chatRequest = new OllamaChatRequest(
                model,
                [new OllamaChatMessage("user", AppendSchemaInstruction(request.Prompt, request.JsonSchema))],
                Stream: false,
                // think:false matches research/ocr-geometry/drafts/parse.py: a reasoning
                // model that ignores it spends the whole output budget on reasoning and
                // returns a truncated JSON document.
                Think: false,
                Format: schemaDocument.RootElement.Clone(),
                Options: new OllamaRequestOptions(
                    Temperature: 0,
                    _options.ContextLength,
                    _options.RowsParserMaximumOutputTokens));

            using var response = await _httpClient.PostAsJsonAsync(
                "api/chat",
                chatRequest,
                JsonOptions,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                RequestRejected((int)response.StatusCode, model);
                throw new ReceiptExtractionException(
                    $"Ollama rejected the request for parser model '{model}' with HTTP "
                    + $"{(int)response.StatusCode}. Check the configured model and retry.");
            }

            var chatResponse = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(
                    JsonOptions,
                    cancellationToken)
                ?? throw new ReceiptExtractionException("Ollama returned an empty response.");
            stopwatch.Stop();

            if (string.IsNullOrWhiteSpace(chatResponse.Message?.Content))
            {
                throw new ReceiptExtractionException(
                    $"Parser model '{model}' returned an empty structured result.");
            }

            RequestCompleted(model, stopwatch.ElapsedMilliseconds, chatResponse.DoneReason ?? "unknown");
            if (string.Equals(chatResponse.DoneReason, "length", StringComparison.OrdinalIgnoreCase))
            {
                // Silence here would look like a short receipt: the JSON is cut off
                // mid-document and the parser would only report "invalid JSON".
                throw new ReceiptExtractionException(
                    $"Parser model '{model}' hit the output limit of {_options.RowsParserMaximumOutputTokens} tokens "
                    + "and returned a truncated result. Raise ReceiptRecognition:RowsParserMaximumOutputTokens.");
            }

            return new StructuredTextResponse(
                chatResponse.Message.Content,
                Id,
                model,
                chatResponse.DoneReason ?? "completed",
                stopwatch.ElapsedMilliseconds);
        }
        catch (ReceiptExtractionException)
        {
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ReceiptExtractionException(
                $"Parser model '{model}' timed out. You can retry processing.");
        }
        catch (HttpRequestException exception)
        {
            RequestFailed(exception);
            throw new ReceiptExtractionException(
                $"Cannot reach local Ollama. Start Ollama and make sure '{model}' is installed.",
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

    public async Task<StructuredTextProviderStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var model = _options.RowsParserModel?.Trim();
        if (string.IsNullOrWhiteSpace(model))
        {
            return new StructuredTextProviderStatus(
                false,
                Id,
                [],
                $"{ReceiptRecognitionOptions.SectionName}:RowsParserModel must not be empty.");
        }

        IReadOnlyList<string> models = [model];
        if (!_options.VerifyModelAvailability
            || string.Equals(_verifiedModel, model, StringComparison.Ordinal))
        {
            return new StructuredTextProviderStatus(true, Id, models, null);
        }

        await _capabilityCheckLock.WaitAsync(cancellationToken);
        try
        {
            if (string.Equals(_verifiedModel, model, StringComparison.Ordinal))
            {
                return new StructuredTextProviderStatus(true, Id, models, null);
            }

            using var response = await _httpClient.GetAsync("api/tags", cancellationToken);
            response.EnsureSuccessStatusCode();
            var tags = await response.Content.ReadFromJsonAsync<OllamaTagsResponse>(
                JsonOptions,
                cancellationToken);
            var installed = tags?.Models
                ?.Select(entry => entry.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
                ?? [];

            if (!ContainsModel(installed, model))
            {
                return new StructuredTextProviderStatus(
                    false,
                    Id,
                    models,
                    $"Install the configured receipt parser model first: ollama pull {model}");
            }

            _verifiedModel = model;
            return new StructuredTextProviderStatus(true, Id, models, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new StructuredTextProviderStatus(false, Id, models, "Ollama model check timed out.");
        }
        catch (HttpRequestException exception)
        {
            return new StructuredTextProviderStatus(
                false,
                Id,
                models,
                exception.StatusCode is null
                    ? "Ollama is not reachable on the configured address."
                    : $"Ollama rejected the model check with HTTP {(int)exception.StatusCode}.");
        }
        catch (JsonException)
        {
            return new StructuredTextProviderStatus(
                false,
                Id,
                models,
                "Ollama returned an invalid response while listing installed models.");
        }
        finally
        {
            _capabilityCheckLock.Release();
        }
    }

    private string RequireModel()
    {
        var model = _options.RowsParserModel?.Trim();
        return string.IsNullOrWhiteSpace(model)
            ? throw new ReceiptExtractionException(
                $"{ReceiptRecognitionOptions.SectionName}:RowsParserModel must name the receipt parser model.")
            : model;
    }

    private async Task EnsureModelInstalledAsync(CancellationToken cancellationToken)
    {
        var status = await GetStatusAsync(cancellationToken);
        if (!status.Available)
        {
            throw new ReceiptExtractionException(
                status.Message ?? "The configured receipt parser model is unavailable.");
        }
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
    {
        if (installed.Contains(required))
        {
            return true;
        }

        var withoutLatest = required.EndsWith(":latest", StringComparison.OrdinalIgnoreCase)
            ? required[..^":latest".Length]
            : required;
        return installed.Contains(withoutLatest) || installed.Contains($"{withoutLatest}:latest");
    }

    private sealed record OllamaChatRequest(
        string Model,
        IReadOnlyList<OllamaChatMessage> Messages,
        bool Stream,
        bool Think,
        JsonElement Format,
        OllamaRequestOptions Options);

    private sealed record OllamaChatMessage(string Role, string Content);

    private sealed record OllamaRequestOptions(
        double Temperature,
        [property: JsonPropertyName("num_ctx")] int NumCtx,
        [property: JsonPropertyName("num_predict")] int NumPredict);

    private sealed record OllamaChatResponse(
        OllamaResponseMessage? Message,
        [property: JsonPropertyName("done_reason")] string? DoneReason);

    private sealed record OllamaResponseMessage(string? Content);
    private sealed record OllamaTagsResponse(IReadOnlyList<OllamaModelTag>? Models);
    private sealed record OllamaModelTag(string Name);

    [LoggerMessage(
        EventId = 2501,
        Level = LogLevel.Information,
        Message = "Ollama text parser completed with model {Model} in {DurationMs} ms ({DoneReason})")]
    private partial void RequestCompleted(string model, long durationMs, string doneReason);

    [LoggerMessage(EventId = 2502, Level = LogLevel.Warning, Message = "Ollama text parser request failed")]
    private partial void RequestFailed(Exception exception);

    [LoggerMessage(EventId = 2503, Level = LogLevel.Warning, Message = "Ollama returned an invalid transport response for the text parser")]
    private partial void InvalidResponse(Exception exception);

    [LoggerMessage(
        EventId = 2504,
        Level = LogLevel.Warning,
        Message = "Ollama returned HTTP {StatusCode} for text parser model {Model}")]
    private partial void RequestRejected(int statusCode, string model);
}

public sealed class StructuredTextProviderResolver : IStructuredTextProviderResolver
{
    private readonly Dictionary<string, IStructuredTextProvider> _providers;

    public StructuredTextProviderResolver(IEnumerable<IStructuredTextProvider> providers)
    {
        var providerList = providers.ToArray();
        var invalid = providerList.FirstOrDefault(provider => string.IsNullOrWhiteSpace(provider.Id));
        if (invalid is not null)
        {
            throw new InvalidOperationException(
                $"Structured text provider '{invalid.GetType().Name}' has an empty identifier.");
        }

        var duplicate = providerList
            .GroupBy(provider => provider.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"More than one structured text provider is registered as '{duplicate.Key}'.");
        }

        _providers = providerList.ToDictionary(provider => provider.Id, StringComparer.OrdinalIgnoreCase);
    }

    public IStructuredTextProvider Resolve(string providerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        return _providers.TryGetValue(providerId.Trim(), out var provider)
            ? provider
            : throw new InvalidOperationException(
                $"Structured text provider '{providerId}' is not registered.");
    }
}
