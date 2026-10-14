using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PurchaseMemory.Application.Analytics;
using PurchaseMemory.Infrastructure.Recognition;

namespace PurchaseMemory.Infrastructure.Analytics;

/// <summary>
/// Plans analytics questions with Claude (Haiku by default) over the same
/// Messages transport as receipt recognition and tag suggestions. The prompt,
/// schema and parser are the shared <see cref="AnalyticsPlanContract"/>, so the
/// plan is exactly what the OpenAI planner would produce. Failures surface as
/// <see cref="AnalyticsPlanningException"/> with a log-safe category; the
/// question, the prompt and the key are never logged.
/// </summary>
public sealed partial class AnthropicAnalyticsQueryPlanner : IAnalyticsQueryPlanner, IDisposable
{
    public const string ProviderId = "anthropic";

    private readonly AnalyticsOptions _options;
    private readonly ILogger<AnthropicAnalyticsQueryPlanner> _logger;
    private readonly IAnthropicMessagesTransport? _testTransport;
    private readonly Func<string?> _readApiKey;
    private readonly Lazy<AnthropicMessagesTransport> _productionTransport;

    public AnthropicAnalyticsQueryPlanner(IOptions<AnalyticsOptions> options, ILogger<AnthropicAnalyticsQueryPlanner> logger)
        : this(options, logger, null,
            () => Environment.GetEnvironmentVariable(AnthropicStructuredVisionProvider.ApiKeyEnvironmentVariable))
    {
    }

    internal AnthropicAnalyticsQueryPlanner(
        IOptions<AnalyticsOptions> options,
        ILogger<AnthropicAnalyticsQueryPlanner> logger,
        IAnthropicMessagesTransport? testTransport,
        Func<string?> readApiKey)
    {
        _options = options.Value;
        _logger = logger;
        _testTransport = testTransport;
        _readApiKey = readApiKey;
        _productionTransport = new Lazy<AnthropicMessagesTransport>(
            () => new AnthropicMessagesTransport(
                _readApiKey() ?? throw new InvalidOperationException("Anthropic API key is not configured."),
                TimeSpan.FromSeconds(_options.RequestTimeoutSeconds),
                _options.HasFallback ? 0 : _options.AnthropicMaximumRetries),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public bool IsConfigured =>
        _options.Enabled
        && !string.IsNullOrWhiteSpace(_options.AnthropicModel)
        && (_testTransport is not null || !string.IsNullOrWhiteSpace(_readApiKey()));

    public string ModelName => _options.AnthropicModel;

    public async Task<AnalyticsPlanningResult> PlanAsync(AnalyticsPlanningRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var model = _options.AnthropicModel;
        if (!IsConfigured)
        {
            RequestFailed(model, _options.Enabled ? "not_configured" : "disabled", 0);
            throw new AnalyticsPlanningException("Analytics planning with Claude is not configured.");
        }

        var codes = new AnalyticsPlanContract.Codebook(request.Vocabulary);
        var stopwatch = Stopwatch.StartNew();
        AnthropicMessagesResult response;
        try
        {
            var transport = _testTransport ?? _productionTransport.Value;
            response = await transport.CreateAsync(
                new AnthropicMessagesRequest(
                    [],
                    AnalyticsPlanContract.BuildPrompt(request, codes),
                    AnalyticsPlanContract.BuildSchema(codes),
                    model,
                    _options.AnthropicEffort,
                    _options.AnthropicMaximumOutputTokens),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is AnthropicProviderException or HttpRequestException or OperationCanceledException or TimeoutException)
        {
            var category = AnthropicFailure.Categorize(exception);
            RequestFailed(model, category, stopwatch.ElapsedMilliseconds);
            throw new AnalyticsPlanningException($"Claude did not answer ({category}).", exception);
        }
        catch (Exception exception)
        {
            // Anything else from the SDK or client setup is still a planning
            // failure (so the fallback planner can answer), never a 500. SDK
            // exceptions never carry the key; the question is not in them.
            var category = $"unexpected:{exception.GetType().Name}";
            RequestFailed(model, category, stopwatch.ElapsedMilliseconds, exception);
            throw new AnalyticsPlanningException($"Claude did not answer ({category}).", exception);
        }

        stopwatch.Stop();
        if (response.StopReason != "end_turn" || string.IsNullOrWhiteSpace(response.Json))
        {
            var category = response.StopReason switch
            {
                "refusal" => $"refusal:{response.RefusalCategory ?? "none"}",
                "end_turn" => "empty",
                var other => $"stop_reason:{other ?? "none"}"
            };
            RequestFailed(model, category, stopwatch.ElapsedMilliseconds);
            throw new AnalyticsPlanningException($"Claude did not complete the plan ({category}).");
        }

        AnalyticsPlanningResult result;
        try
        {
            result = AnalyticsPlanContract.ToResult(response.Json, codes, request, response.InputTokens, response.OutputTokens);
        }
        catch (AnalyticsPlanningException)
        {
            RequestFailed(model, "malformed_plan", stopwatch.ElapsedMilliseconds);
            throw;
        }

        var cost = AnthropicPricing.EstimateCostUsd(model, response.InputTokens, response.OutputTokens);
        RequestCompleted(model, _options.AnthropicEffort, stopwatch.ElapsedMilliseconds, response.MessageId,
            response.InputTokens, response.OutputTokens, cost);
        return result;
    }

    public void Dispose()
    {
        if (_productionTransport.IsValueCreated)
        {
            _productionTransport.Value.Dispose();
        }
    }

    [LoggerMessage(
        EventId = 3421,
        Level = LogLevel.Information,
        Message = "Analytics plan completed with model {Model} (effort {Effort}) in {DurationMs} ms; message {MessageId}; input tokens {InputTokens}; output tokens {OutputTokens}; estimated cost USD {CostUsd}")]
    private partial void RequestCompleted(
        string model,
        AnthropicEffort effort,
        long durationMs,
        string? messageId,
        long? inputTokens,
        long? outputTokens,
        decimal? costUsd);

    [LoggerMessage(
        EventId = 3422,
        Level = LogLevel.Warning,
        Message = "Analytics planning with model {Model} failed after {DurationMs} ms with category {Category}")]
    private partial void RequestFailed(string model, string category, long durationMs, Exception? exception = null);
}
