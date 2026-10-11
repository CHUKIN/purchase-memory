using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PurchaseMemory.Application.Receipts;

namespace PurchaseMemory.Infrastructure.Recognition;

/// <summary>
/// Claude (Anthropic Messages API) behind the shared structured-vision
/// contract: the same prompt, schema (adapted to the supported subset), parser
/// and repair retry as OpenAI. Every failure surfaces as
/// <see cref="ReceiptExtractionException"/>, which is what lets the router
/// fall back to the next pipeline (OpenAI in production).
/// </summary>
public sealed partial class AnthropicStructuredVisionProvider : IStructuredVisionProvider, IDisposable
{
    public const string ProviderId = "anthropic";
    public const string ApiKeyEnvironmentVariable = "ANTHROPIC_API_KEY";

    private readonly ReceiptRecognitionOptions _options;
    private readonly ILogger<AnthropicStructuredVisionProvider> _logger;
    private readonly IAnthropicMessagesTransport? _testTransport;
    private readonly Func<string?> _readApiKey;
    private readonly Lazy<AnthropicMessagesTransport> _productionTransport;

    public AnthropicStructuredVisionProvider(
        IOptions<ReceiptRecognitionOptions> options,
        ILogger<AnthropicStructuredVisionProvider> logger)
        : this(options, logger, null, () => Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable))
    {
    }

    internal AnthropicStructuredVisionProvider(
        IOptions<ReceiptRecognitionOptions> options,
        ILogger<AnthropicStructuredVisionProvider> logger,
        IAnthropicMessagesTransport? testTransport,
        Func<string?> readApiKey)
    {
        _options = options.Value;
        _logger = logger;
        _testTransport = testTransport;
        _readApiKey = readApiKey;
        _productionTransport = new Lazy<AnthropicMessagesTransport>(
            () => new AnthropicMessagesTransport(
                _readApiKey()
                    ?? throw new InvalidOperationException("Anthropic API key is not configured."),
                TimeSpan.FromSeconds(_options.AnthropicRequestTimeoutSeconds),
                _options.AnthropicMaximumRetries),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public string Id => ProviderId;

    public async Task<StructuredVisionResponse> GenerateAsync(
        StructuredVisionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Images.Count == 0 || request.Images.Any(image => image.Content.IsEmpty))
        {
            throw new ReceiptExtractionException("The receipt images are empty.");
        }

        if (_testTransport is null && string.IsNullOrWhiteSpace(_readApiKey()))
        {
            RequestFailed(_options.AnthropicModel, "not_configured", 0);
            throw new ReceiptExtractionException(
                $"Claude is not configured. Set {ApiKeyEnvironmentVariable} on the backend.");
        }

        if (string.IsNullOrWhiteSpace(_options.AnthropicModel))
        {
            throw new ReceiptExtractionException("The Claude model is not configured.");
        }

        var model = _options.AnthropicModel;
        var transport = _testTransport ?? _productionTransport.Value;
        var stopwatch = Stopwatch.StartNew();
        AnthropicMessagesResult response;
        try
        {
            response = await transport.CreateAsync(
                new AnthropicMessagesRequest(
                    request.Images,
                    request.Prompt,
                    request.JsonSchema,
                    model,
                    _options.AnthropicEffort,
                    _options.AnthropicMaximumOutputTokens),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is AnthropicServiceException or HttpRequestException or OperationCanceledException or TimeoutException)
        {
            stopwatch.Stop();
            var (category, message) = Classify(exception);
            RequestFailed(model, category, stopwatch.ElapsedMilliseconds);
            throw new ReceiptExtractionException(message, exception);
        }

        stopwatch.Stop();
        var cost = AnthropicPricing.EstimateCostUsd(model, response.InputTokens, response.OutputTokens);
        switch (response.StopReason)
        {
            case "end_turn":
                break;
            case "refusal":
                // Haiku has no server-side refusal fallback: the router's OpenAI
                // fallback is the safety net. The category is a short enum value.
                RequestFailed(model, $"refusal:{response.RefusalCategory ?? "none"}", stopwatch.ElapsedMilliseconds);
                throw new ReceiptExtractionException(
                    "Claude declined to analyze this receipt. Retry or choose another provider.");
            case "max_tokens":
                RequestFailed(model, "max_tokens", stopwatch.ElapsedMilliseconds);
                throw new ReceiptExtractionException(
                    "Claude returned an incomplete result (output limit reached).");
            default:
                RequestFailed(model, $"stop_reason:{response.StopReason ?? "none"}", stopwatch.ElapsedMilliseconds);
                throw new ReceiptExtractionException(
                    $"Claude did not complete the request (stop reason: {response.StopReason ?? "unknown"}).");
        }

        if (string.IsNullOrWhiteSpace(response.Json))
        {
            RequestFailed(model, "empty", stopwatch.ElapsedMilliseconds);
            throw new ReceiptExtractionException("Claude returned an empty structured result.");
        }

        RequestCompleted(
            model,
            _options.AnthropicEffort,
            stopwatch.ElapsedMilliseconds,
            response.MessageId,
            response.InputTokens,
            response.OutputTokens,
            cost);
        return new StructuredVisionResponse(
            response.Json,
            Id,
            model,
            response.MessageId,
            response.StopReason,
            response.InputTokens,
            response.OutputTokens,
            stopwatch.ElapsedMilliseconds);
    }

    private static (string Category, string Message) Classify(Exception exception)
    {
        var statusCode = exception is AnthropicServiceException service ? service.StatusCode : 0;
        return statusCode switch
        {
            0 when ContainsInner<OperationCanceledException>(exception) || ContainsInner<TimeoutException>(exception) =>
                ("timeout", "Claude recognition timed out. Retry or choose another provider."),
            0 => ("network", "Claude could not be reached. Check the network and retry."),
            401 or 403 => ("unauthorized", $"Claude rejected the backend credential. Check {ApiKeyEnvironmentVariable}."),
            429 => ("rate_limited", "Claude rate limit was reached. Retry later or choose another provider."),
            529 => ("overloaded", "Claude is overloaded right now. Retry later or choose another provider."),
            >= 500 => ($"http_{statusCode}", "Claude is temporarily unavailable. Retry or choose another provider."),
            _ => ($"http_{statusCode}", "Claude rejected the recognition request. Retry or choose another provider.")
        };
    }

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

    public void Dispose()
    {
        if (_productionTransport.IsValueCreated)
        {
            _productionTransport.Value.Dispose();
        }
    }

    public Task<StructuredVisionProviderStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(_options.AnthropicModel))
        {
            return Task.FromResult(new StructuredVisionProviderStatus(
                false,
                Id,
                [],
                "Claude model is not configured."));
        }

        if (string.IsNullOrWhiteSpace(_readApiKey()))
        {
            return Task.FromResult(new StructuredVisionProviderStatus(
                false,
                Id,
                [_options.AnthropicModel],
                $"Set {ApiKeyEnvironmentVariable} on the backend."));
        }

        return Task.FromResult(new StructuredVisionProviderStatus(
            true,
            Id,
            [_options.AnthropicModel],
            "Configured. No paid model call was made."));
    }

    [LoggerMessage(
        EventId = 2221,
        Level = LogLevel.Information,
        Message = "Anthropic provider completed with model {Model} (effort {Effort}) in {DurationMs} ms; message {MessageId}; input tokens {InputTokens}; output tokens {OutputTokens}; estimated cost USD {CostUsd}")]
    private partial void RequestCompleted(
        string model,
        AnthropicEffort effort,
        long durationMs,
        string? messageId,
        long? inputTokens,
        long? outputTokens,
        decimal? costUsd);

    [LoggerMessage(
        EventId = 2222,
        Level = LogLevel.Warning,
        Message = "Anthropic provider request with model {Model} failed after {DurationMs} ms with category {Category}")]
    private partial void RequestFailed(string model, string category, long durationMs);
}

/// <summary>
/// List prices used only for the cost estimate in the log (USD per 1M tokens,
/// prompts up to 100K tokens; a receipt is ~3K). Unknown models log no estimate.
/// </summary>
internal static class AnthropicPricing
{
    private static readonly Dictionary<string, (decimal Input, decimal Output)> Prices =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["claude-haiku-5-5"] = (0.10m, 0.50m)
        };

    public static decimal? EstimateCostUsd(string model, long? inputTokens, long? outputTokens) =>
        Prices.TryGetValue(model, out var price) && inputTokens is not null && outputTokens is not null
            ? decimal.Round((inputTokens.Value * price.Input + outputTokens.Value * price.Output) / 1_000_000m, 6)
            : null;
}
