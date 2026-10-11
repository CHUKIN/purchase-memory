using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using PurchaseMemory.Application.Receipts;

namespace PurchaseMemory.Infrastructure.Recognition;

internal sealed record AnthropicMessagesRequest(
    IReadOnlyList<ReceiptImageData> Images,
    string Prompt,
    string JsonSchema,
    string Model,
    AnthropicEffort Effort,
    int MaximumOutputTokens);

internal sealed record AnthropicMessagesResult(
    string? Json,
    string? StopReason,
    string? RefusalCategory,
    string? MessageId,
    long? InputTokens,
    long? OutputTokens);

internal interface IAnthropicMessagesTransport
{
    Task<AnthropicMessagesResult> CreateAsync(
        AnthropicMessagesRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// A failed Messages API call. <see cref="StatusCode"/> is 0 when no HTTP
/// response arrived (network failure or timeout).
/// </summary>
internal sealed class AnthropicServiceException : Exception
{
    public AnthropicServiceException(int statusCode, string message, Exception innerException)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}

/// <summary>
/// Short, log-safe failure categories for a Messages API call, shared by every
/// Claude feature (receipts, tag suggestions) so their logs read the same.
/// </summary>
internal static class AnthropicFailure
{
    public static int StatusCode(Exception exception) =>
        exception is AnthropicServiceException service ? service.StatusCode : 0;

    public static string Categorize(Exception exception) => StatusCode(exception) switch
    {
        0 when ContainsInner<OperationCanceledException>(exception) || ContainsInner<TimeoutException>(exception) => "timeout",
        0 => "network",
        401 or 403 => "unauthorized",
        429 => "rate_limited",
        529 => "overloaded",
        var status => $"http_{status}"
    };

    private static bool ContainsInner<TException>(Exception exception)
        where TException : Exception
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is TException)
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// The only type that knows about the official Anthropic SDK. Keeping it
/// internal keeps SDK types out of the Application contracts, the same way
/// <see cref="OpenAiResponsesTransport"/> isolates the OpenAI SDK. Requests
/// without images are plain text calls (tag suggestions use that).
/// </summary>
internal sealed class AnthropicMessagesTransport : IAnthropicMessagesTransport, IDisposable
{
    // Claude API limits (platform.claude.com/docs/en/build-with-claude/vision):
    // 10 MB per base64 image and 32 MB per request. Receipt originals may be up
    // to 15 MB each, so an oversized photo is refused here, before any upload,
    // and the router sends it to the fallback (OpenAI) instead.
    internal const long MaximumBase64BytesPerImage = 10L * 1024 * 1024;
    internal const long MaximumBase64BytesPerRequest = 30L * 1024 * 1024;

    private readonly AnthropicClient _client;
    private readonly HttpClient _httpClient;

    public AnthropicMessagesTransport(
        string apiKey,
        TimeSpan timeout,
        int maximumRetries,
        HttpMessageHandler? messageHandler = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        // The SDK enforces its own per-attempt timeout; the HttpClient one is
        // left infinite so the two cannot race with different messages.
        _httpClient = messageHandler is null
            ? new HttpClient()
            : new HttpClient(messageHandler, disposeHandler: false);
        _httpClient.Timeout = Timeout.InfiniteTimeSpan;
        _client = new AnthropicClient
        {
            ApiKey = apiKey,
            HttpClient = _httpClient,
            Timeout = timeout,
            MaxRetries = maximumRetries
        };
    }

    public async Task<AnthropicMessagesResult> CreateAsync(
        AnthropicMessagesRequest request,
        CancellationToken cancellationToken)
    {
        EnsureWithinSizeLimits(request.Images);
        var content = new List<ContentBlockParam>
        {
            new TextBlockParam { Text = request.Prompt }
        };
        content.AddRange(request.Images.Select(image => (ContentBlockParam)new ImageBlockParam
        {
            Source = new Base64ImageSource
            {
                Data = Convert.ToBase64String(image.Content.Span),
                MediaType = MapMediaType(image.ContentType)
            }
        }));

        var parameters = new MessageCreateParams
        {
            Model = request.Model,
            MaxTokens = request.MaximumOutputTokens,
            Thinking = new ThinkingConfigAdaptive(),
            OutputConfig = new OutputConfig
            {
                Effort = request.Effort switch
                {
                    AnthropicEffort.Low => Effort.Low,
                    AnthropicEffort.Medium => Effort.Medium,
                    AnthropicEffort.High => Effort.High,
                    _ => throw new ArgumentOutOfRangeException(nameof(request), "Unknown Anthropic effort.")
                },
                Format = new JsonOutputFormat
                {
                    Schema = AnthropicJsonSchemaAdapter.Adapt(request.JsonSchema)
                }
            },
            Messages = [new MessageParam { Role = Role.User, Content = content }]
        };

        try
        {
            var message = await _client.Messages.Create(parameters, cancellationToken);
            var json = message.Content
                .Select(block => block.TryPickText(out var text) ? text.Text : null)
                .FirstOrDefault(text => text is not null);
            return new AnthropicMessagesResult(
                json,
                message.StopReason?.Raw(),
                message.StopDetails?.Category?.Raw(),
                message.ID,
                message.Usage.InputTokens,
                message.Usage.OutputTokens);
        }
        catch (AnthropicApiException exception)
        {
            throw new AnthropicServiceException(
                (int)exception.StatusCode,
                "The Anthropic service rejected the request.",
                exception);
        }
        catch (AnthropicException exception)
        {
            throw new AnthropicServiceException(
                0,
                "The Anthropic service could not be reached.",
                exception);
        }
    }

    internal static void EnsureWithinSizeLimits(IReadOnlyList<ReceiptImageData> images)
    {
        var sizes = images.Select(image => Base64Length(image.Content.Length)).ToArray();
        if (sizes.Any(size => size > MaximumBase64BytesPerImage) || sizes.Sum() > MaximumBase64BytesPerRequest)
        {
            throw new ReceiptExtractionException(
                "The receipt photos are too large for Claude (10 MB per photo, 30 MB in total after encoding).");
        }
    }

    private static long Base64Length(long bytes) => (bytes + 2) / 3 * 4;

    internal static string MapMediaType(string contentType) =>
        contentType.Trim().ToLowerInvariant() switch
        {
            "image/jpeg" or "image/jpg" => "image/jpeg",
            "image/png" => "image/png",
            "image/webp" => "image/webp",
            "image/gif" => "image/gif",
            _ => throw new ReceiptExtractionException(
                $"Claude does not accept receipt images of type '{contentType}'.")
        };

    public void Dispose() => _httpClient.Dispose();
}

/// <summary>
/// Turns the shared receipt schema into the subset Anthropic structured
/// outputs accept. Numeric, length, count and pattern keywords are rejected
/// there, so they are removed; the shape (types, required, enums,
/// additionalProperties) is unchanged. The removed limits are not lost: the
/// shared <see cref="ReceiptStructuredOutputParser"/> enforces every one that
/// matters for receipt data (lengths, non-negative totals and prices, positive
/// quantity, currency format, item count), and bounding boxes outside the photo
/// degrade to null.
/// </summary>
internal static class AnthropicJsonSchemaAdapter
{
    private static readonly HashSet<string> UnsupportedKeywords = new(StringComparer.Ordinal)
    {
        "minimum",
        "maximum",
        "exclusiveMinimum",
        "exclusiveMaximum",
        "multipleOf",
        "minLength",
        "maxLength",
        "pattern",
        "minItems",
        "maxItems",
        "$schema",
        "$id",
        "title"
    };

    public static IReadOnlyDictionary<string, JsonElement> Adapt(string jsonSchema)
    {
        using var document = JsonDocument.Parse(jsonSchema);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("The JSON schema must be an object.", nameof(jsonSchema));
        }

        return Strip(document.RootElement).EnumerateObject().ToDictionary(
            property => property.Name,
            property => property.Value.Clone(),
            StringComparer.Ordinal);
    }

    public static string AdaptToJson(string jsonSchema) =>
        JsonSerializer.Serialize(Adapt(jsonSchema));

    // Keyword names are only meaningful as schema keys, but "properties" maps
    // field names to schemas: a receipt field that happened to be called
    // "pattern" must survive. Under "properties" every key is kept.
    private static JsonElement Strip(JsonElement element, bool keysAreFieldNames = false)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    if (!keysAreFieldNames && UnsupportedKeywords.Contains(property.Name))
                    {
                        continue;
                    }

                    result[property.Name] = Strip(
                        property.Value,
                        keysAreFieldNames: !keysAreFieldNames && property.Name == "properties");
                }

                return JsonSerializer.SerializeToElement(result);
            case JsonValueKind.Array:
                return JsonSerializer.SerializeToElement(
                    element.EnumerateArray().Select(item => Strip(item)).ToArray());
            default:
                return element.Clone();
        }
    }
}
