using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PurchaseMemory.Application.Receipts.TextLocation;
using PurchaseMemory.Domain.Receipts;

namespace PurchaseMemory.Infrastructure.TextLocation;

public sealed class AzureDocumentIntelligenceOptions
{
    public const string SectionName = "TextLocation:Azure";

    public string ApiVersion { get; set; } = "2024-11-30";

    /// <summary>Per HTTP request.</summary>
    public int RequestTimeoutSeconds { get; set; } = 30;

    /// <summary>The free tier (F0) accepts photos up to 4 MB.</summary>
    public long MaximumImageBytes { get; set; } = 4L * 1024 * 1024;

    /// <summary>Minimum gap between any two requests (F0 allows about one per second).</summary>
    public int RequestSpacingMilliseconds { get; set; } = 1100;

    public int FirstPollDelayMilliseconds { get; set; } = 3000;

    public int PollIntervalMilliseconds { get; set; } = 1500;

    public int MaximumPolls { get; set; } = 30;

    /// <summary>Waits on "429 Too Many Requests" per receipt before giving up for now.</summary>
    public int MaximumRateLimitRetries { get; set; } = 3;

    /// <summary>A Retry-After longer than this is not waited out inside one receipt.</summary>
    public int MaximumRetryAfterSeconds { get; set; } = 60;
}

/// <summary>
/// Azure AI Document Intelligence <c>prebuilt-read</c> over REST (v4.0 GA):
/// POST the photo, poll the operation, keep the first page's lines and words.
/// Endpoint and key come only from <c>AZURE_DOCINTEL_ENDPOINT</c> /
/// <c>AZURE_DOCINTEL_KEY</c>; without them nothing is ever sent. The key is
/// sent only to the configured host and is never logged, nor are photos or
/// recognised text — logs carry the stage, HTTP status, Azure's short error
/// code and timings.
/// </summary>
public sealed partial class AzureDocumentIntelligenceTextLocator : IReceiptTextLocator
{
    public const string EndpointVariable = "AZURE_DOCINTEL_ENDPOINT";
    public const string KeyVariable = "AZURE_DOCINTEL_KEY";
    public const string ModelId = "prebuilt-read";

    /// <summary>
    /// Name of the HTTP client registration. Its handler must not follow
    /// redirects: the subscription key header would go along to another host.
    /// </summary>
    public const string HttpClientName = "AzureDocumentIntelligence";

    /// <summary>The primary handler for <see cref="HttpClientName"/>.</summary>
    public static HttpMessageHandler CreatePrimaryHandler() => new SocketsHttpHandler { AllowAutoRedirect = false };

    private readonly HttpClient _httpClient;
    private readonly AzureDocumentIntelligenceOptions _options;
    private readonly ILogger<AzureDocumentIntelligenceTextLocator> _logger;
    private readonly Func<string?> _readEndpoint;
    private readonly Func<string?> _readKey;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Stopwatch _sinceLastRequest = new();

    public AzureDocumentIntelligenceTextLocator(
        HttpClient httpClient,
        IOptions<AzureDocumentIntelligenceOptions> options,
        ILogger<AzureDocumentIntelligenceTextLocator> logger)
        : this(
            httpClient,
            options,
            logger,
            () => Environment.GetEnvironmentVariable(EndpointVariable),
            () => Environment.GetEnvironmentVariable(KeyVariable),
            Task.Delay)
    {
    }

    internal AzureDocumentIntelligenceTextLocator(
        HttpClient httpClient,
        IOptions<AzureDocumentIntelligenceOptions> options,
        ILogger<AzureDocumentIntelligenceTextLocator> logger,
        Func<string?> readEndpoint,
        Func<string?> readKey,
        Func<TimeSpan, CancellationToken, Task> delay)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _readEndpoint = readEndpoint;
        _readKey = readKey;
        _delay = delay;
        _httpClient.Timeout = TimeSpan.FromSeconds(Math.Clamp(_options.RequestTimeoutSeconds, 5, 300));
    }

    public bool IsConfigured => TryReadEndpoint(out _) && !string.IsNullOrWhiteSpace(_readKey());

    public long MaximumImageBytes => _options.MaximumImageBytes;

    public string Source => $"azure:{ModelId}:{_options.ApiVersion}";

    public async Task<ReceiptTextLocatorResult> ReadAsync(
        ReadOnlyMemory<byte> image,
        string contentType,
        CancellationToken cancellationToken)
    {
        var key = _readKey();
        if (!TryReadEndpoint(out var endpoint) || string.IsNullOrWhiteSpace(key))
        {
            return ReceiptTextLocatorResult.Failed(ReceiptTextLocatorFailure.NotAuthorized);
        }

        if (image.IsEmpty)
        {
            return ReceiptTextLocatorResult.Failed(ReceiptTextLocatorFailure.Rejected);
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var submitted = await SubmitAsync(endpoint, key, image, cancellationToken);
            if (submitted.Failure is not null)
            {
                return submitted.Failure;
            }

            return await PollAsync(endpoint, key, submitted.Operation!, stopwatch, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        // A truncated or malformed answer (JsonException, HttpIOException) is a
        // passing failure like a network error, never an exception for the caller.
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or TimeoutException
            or JsonException or IOException)
        {
            RequestFailed("network", 0, exception.GetType().Name, stopwatch.ElapsedMilliseconds);
            return ReceiptTextLocatorResult.Failed(ReceiptTextLocatorFailure.Transient);
        }
    }

    private async Task<(Uri? Operation, ReceiptTextLocatorResult? Failure)> SubmitAsync(
        Uri endpoint,
        string key,
        ReadOnlyMemory<byte> image,
        CancellationToken cancellationToken)
    {
        var analyzeUri = new Uri(
            endpoint,
            $"documentintelligence/documentModels/{ModelId}:analyze?api-version={Uri.EscapeDataString(_options.ApiVersion)}");
        TimeSpan? lastRetryAfter = null;
        for (var attempt = 0; attempt <= _options.MaximumRateLimitRetries; attempt++)
        {
            await PaceAsync(cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Post, analyzeUri);
            request.Headers.Add("Ocp-Apim-Subscription-Key", key);
            request.Content = new ReadOnlyMemoryContent(image);
            // Azure detects the format from the bytes; octet-stream avoids a 415 on a mislabelled type.
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Accepted)
            {
                var operation = response.Headers.TryGetValues("Operation-Location", out var values)
                    ? values.FirstOrDefault()
                    : null;
                if (!Uri.TryCreate(operation, UriKind.Absolute, out var operationUri) || !SameService(endpoint, operationUri))
                {
                    // The key may only ever go back to the configured service.
                    RequestFailed("analyze", 202, "unexpected_operation_location", 0);
                    return (null, ReceiptTextLocatorResult.Failed(ReceiptTextLocatorFailure.Transient));
                }

                return (operationUri, null);
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                lastRetryAfter = RetryAfter(response) ?? TimeSpan.FromSeconds(10);
                RateLimited("analyze", (int)lastRetryAfter.Value.TotalSeconds);
                if (attempt < _options.MaximumRateLimitRetries && lastRetryAfter.Value.TotalSeconds <= _options.MaximumRetryAfterSeconds)
                {
                    await _delay(lastRetryAfter.Value, cancellationToken);
                    continue;
                }

                break;
            }

            var code = await ErrorCodeAsync(response, cancellationToken);
            RequestFailed("analyze", (int)response.StatusCode, code, 0);
            return (null, ReceiptTextLocatorResult.Failed(Classify(response.StatusCode)));
        }

        return (null, ReceiptTextLocatorResult.Failed(ReceiptTextLocatorFailure.RateLimited, lastRetryAfter));
    }

    private async Task<ReceiptTextLocatorResult> PollAsync(
        Uri endpoint,
        string key,
        Uri operation,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        await _delay(TimeSpan.FromMilliseconds(_options.FirstPollDelayMilliseconds), cancellationToken);
        var rateLimitWaits = 0;
        for (var poll = 0; poll < _options.MaximumPolls; poll++)
        {
            await PaceAsync(cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Get, operation);
            request.Headers.Add("Ocp-Apim-Subscription-Key", key);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var wait = RetryAfter(response) ?? TimeSpan.FromSeconds(5);
                RateLimited("poll", (int)wait.TotalSeconds);
                if (++rateLimitWaits > _options.MaximumRateLimitRetries || wait.TotalSeconds > _options.MaximumRetryAfterSeconds)
                {
                    return ReceiptTextLocatorResult.Failed(ReceiptTextLocatorFailure.RateLimited, wait);
                }

                await _delay(wait, cancellationToken);
                continue;
            }

            if (response.StatusCode != HttpStatusCode.OK)
            {
                var code = await ErrorCodeAsync(response, cancellationToken);
                RequestFailed("poll", (int)response.StatusCode, code, stopwatch.ElapsedMilliseconds);
                // A 404 on our own operation is not a refused photo: try the receipt again later.
                return ReceiptTextLocatorResult.Failed(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                    ? ReceiptTextLocatorFailure.NotAuthorized
                    : ReceiptTextLocatorFailure.Transient);
            }

            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
            var status = document.RootElement.TryGetProperty("status", out var statusElement)
                ? statusElement.GetString()
                : null;
            switch (status)
            {
                case "succeeded":
                    var page = ParsePage(document.RootElement, Source);
                    if (page is null)
                    {
                        RequestFailed("result", 200, "no_pixel_page", stopwatch.ElapsedMilliseconds);
                        return ReceiptTextLocatorResult.Failed(ReceiptTextLocatorFailure.Rejected);
                    }

                    AnalysisCompleted(page.Lines.Count, poll + 1, stopwatch.ElapsedMilliseconds);
                    return ReceiptTextLocatorResult.Read(page);
                case "failed":
                case "canceled":
                case "skipped":
                    RequestFailed("result", 200, SafeCode(ErrorCode(document.RootElement) ?? status), stopwatch.ElapsedMilliseconds);
                    return ReceiptTextLocatorResult.Failed(ReceiptTextLocatorFailure.Rejected);
                default:
                    await _delay(RetryAfter(response) ?? TimeSpan.FromMilliseconds(_options.PollIntervalMilliseconds), cancellationToken);
                    break;
            }
        }

        RequestFailed("poll", 200, "still_running", stopwatch.ElapsedMilliseconds);
        return ReceiptTextLocatorResult.Failed(ReceiptTextLocatorFailure.Transient);
    }

    /// <summary>
    /// The first page in pixels: its lines with their words, words assigned to
    /// a line by the text span they cover (as Azure links them). Returns null
    /// for a page that is not measured in pixels (a PDF, never a photo).
    /// </summary>
    internal static ReceiptTextPage? ParsePage(JsonElement root, string source)
    {
        if (!root.TryGetProperty("analyzeResult", out var result)
            || !result.TryGetProperty("pages", out var pages)
            || pages.ValueKind != JsonValueKind.Array
            || pages.GetArrayLength() == 0)
        {
            return null;
        }

        var page = pages[0];
        if (page.TryGetProperty("unit", out var unit) && unit.GetString() is { } unitName
            && !string.Equals(unitName, "pixel", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!page.TryGetProperty("width", out var widthElement) || !page.TryGetProperty("height", out var heightElement)
            || !widthElement.TryGetDouble(out var width) || !heightElement.TryGetDouble(out var height)
            || width < 1 || height < 1)
        {
            return null;
        }

        var words = new List<(ReceiptTextWord Word, int Offset, int Length)>();
        foreach (var word in Array(page, "words"))
        {
            if (Polygon(word) is { } polygon && Span(word) is { } span)
            {
                words.Add((new ReceiptTextWord(Text(word), polygon), span.Offset, span.Length));
            }
        }

        var lines = new List<ReceiptTextLine>();
        foreach (var line in Array(page, "lines"))
        {
            if (Polygon(line) is not { } polygon)
            {
                continue;
            }

            var spans = Array(line, "spans").Select(SpanOf).OfType<(int Offset, int Length)>().ToList();
            var lineWords = words
                .Where(word => spans.Any(span => span.Offset <= word.Offset && word.Offset + word.Length <= span.Offset + span.Length))
                .Select(word => word.Word)
                .ToList();
            lines.Add(new ReceiptTextLine(Text(line), polygon, lineWords));
        }

        return new ReceiptTextPage(source, width, height, lines);
    }

    private static JsonElement[] Array(JsonElement element, string name) =>
        element.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array
            ? [.. array.EnumerateArray()]
            : [];

    private static string Text(JsonElement element) =>
        element.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String
            ? content.GetString() ?? string.Empty
            : string.Empty;

    private static double[]? Polygon(JsonElement element)
    {
        if (!element.TryGetProperty("polygon", out var polygon) || polygon.ValueKind != JsonValueKind.Array
            || polygon.GetArrayLength() != 8)
        {
            return null;
        }

        var points = new double[8];
        var index = 0;
        foreach (var value in polygon.EnumerateArray())
        {
            if (!value.TryGetDouble(out var number) || !double.IsFinite(number))
            {
                return null;
            }

            points[index++] = Math.Round(number, 1);
        }

        return points;
    }

    private static (int Offset, int Length)? Span(JsonElement element) =>
        element.TryGetProperty("span", out var span) ? SpanOf(span) : null;

    private static (int Offset, int Length)? SpanOf(JsonElement span) =>
        span.TryGetProperty("offset", out var offset) && span.TryGetProperty("length", out var length)
        && offset.TryGetInt32(out var start) && length.TryGetInt32(out var size)
            ? (start, size)
            : null;

    private bool TryReadEndpoint(out Uri endpoint)
    {
        var value = _readEndpoint();
        if (!string.IsNullOrWhiteSpace(value)
            && Uri.TryCreate(value.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var parsed)
            && parsed.Scheme == Uri.UriSchemeHttps
            && string.IsNullOrEmpty(parsed.Query))
        {
            endpoint = parsed;
            return true;
        }

        endpoint = null!;
        return false;
    }

    private static bool SameService(Uri endpoint, Uri operation) =>
        operation.Scheme == Uri.UriSchemeHttps
        && string.Equals(operation.Host, endpoint.Host, StringComparison.OrdinalIgnoreCase)
        && operation.Port == endpoint.Port;

    private async Task PaceAsync(CancellationToken cancellationToken)
    {
        if (_sinceLastRequest.IsRunning)
        {
            var wait = TimeSpan.FromMilliseconds(_options.RequestSpacingMilliseconds) - _sinceLastRequest.Elapsed;
            if (wait > TimeSpan.Zero)
            {
                await _delay(wait, cancellationToken);
            }
        }

        _sinceLastRequest.Restart();
    }

    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header?.Delta is { } delta)
        {
            return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
        }

        if (header?.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            return wait < TimeSpan.Zero ? TimeSpan.Zero : wait;
        }

        return null;
    }

    private static ReceiptTextLocatorFailure Classify(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound => ReceiptTextLocatorFailure.NotAuthorized,
        HttpStatusCode.BadRequest or HttpStatusCode.UnsupportedMediaType or HttpStatusCode.RequestEntityTooLarge => ReceiptTextLocatorFailure.Rejected,
        _ => ReceiptTextLocatorFailure.Transient
    };

    private static async Task<string> ErrorCodeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(text))
            {
                return "none";
            }

            using var document = JsonDocument.Parse(text);
            return SafeCode(ErrorCode(document.RootElement));
        }
        catch (JsonException)
        {
            return "unreadable";
        }
    }

    /// <summary>Azure's own short error code (inner one when present), e.g. <c>InvalidContentLength</c>.</summary>
    private static string? ErrorCode(JsonElement root)
    {
        if (!root.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (error.TryGetProperty("innererror", out var inner) && inner.ValueKind == JsonValueKind.Object
            && inner.TryGetProperty("code", out var innerCode) && innerCode.ValueKind == JsonValueKind.String)
        {
            return innerCode.GetString();
        }

        return error.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String ? code.GetString() : null;
    }

    /// <summary>Only letters, digits and "_" (at most 60) reach the log: never free text from a response.</summary>
    private static string SafeCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return "none";
        }

        var safe = new string(code.Where(character => char.IsAsciiLetterOrDigit(character) || character == '_').Take(60).ToArray());
        return safe.Length == 0 ? "none" : safe;
    }

    [LoggerMessage(EventId = 3310, Level = LogLevel.Warning,
        Message = "Azure text location {Stage} failed: HTTP {StatusCode} {ErrorCode} after {ElapsedMs} ms")]
    private partial void RequestFailed(string stage, int statusCode, string errorCode, long elapsedMs);

    [LoggerMessage(EventId = 3311, Level = LogLevel.Information,
        Message = "Azure text location {Stage} rate limited; retry after {RetryAfterSeconds} s")]
    private partial void RateLimited(string stage, int retryAfterSeconds);

    [LoggerMessage(EventId = 3312, Level = LogLevel.Debug,
        Message = "Azure text location read {LineCount} lines after {Polls} polls in {ElapsedMs} ms")]
    private partial void AnalysisCompleted(int lineCount, int polls, long elapsedMs);
}
