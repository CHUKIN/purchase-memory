using Microsoft.Extensions.Options;
using PurchaseMemory.Application.Analytics;
using PurchaseMemory.Infrastructure.Categorization;
using PurchaseMemory.Infrastructure.Recognition;

namespace PurchaseMemory.Infrastructure.Analytics;

/// <summary>
/// Asks a cheap OpenAI model to translate a question into an analytics plan,
/// using the shared <see cref="AnalyticsPlanContract"/> (prompt, strict schema, parser).
/// </summary>
public sealed class OpenAiAnalyticsQueryPlanner : IAnalyticsQueryPlanner, IDisposable
{
    public const string ProviderId = "openai";

    private const string SchemaName = "purchase_memory_analytics_plan_v1";
    private const string SchemaDescription = "A query plan over a household's purchase history.";

    private readonly AnalyticsOptions _options;
    private readonly IOpenAiTextTransport? _testTransport;
    private readonly Func<string?> _readApiKey;
    private readonly Lazy<OpenAiTextTransport> _productionTransport;

    public OpenAiAnalyticsQueryPlanner(IOptions<AnalyticsOptions> options)
        : this(options, null, () => Environment.GetEnvironmentVariable(OpenAiStructuredVisionProvider.ApiKeyEnvironmentVariable))
    {
    }

    internal OpenAiAnalyticsQueryPlanner(
        IOptions<AnalyticsOptions> options,
        IOpenAiTextTransport? testTransport,
        Func<string?> readApiKey)
    {
        _options = options.Value;
        _testTransport = testTransport;
        _readApiKey = readApiKey;
        _productionTransport = new Lazy<OpenAiTextTransport>(
            () => new OpenAiTextTransport(
                _readApiKey() ?? throw new InvalidOperationException("OpenAI API key is not configured."),
                _options.OpenAiModel,
                TimeSpan.FromSeconds(_options.RequestTimeoutSeconds),
                _options.HasFallback ? 0 : null),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public bool IsConfigured =>
        _options.Enabled
        && !string.IsNullOrWhiteSpace(_options.OpenAiModel)
        && (_testTransport is not null || !string.IsNullOrWhiteSpace(_readApiKey()));

    public string ModelName => _options.OpenAiModel;

    public async Task<AnalyticsPlanningResult> PlanAsync(AnalyticsPlanningRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsConfigured)
        {
            throw new AnalyticsPlanningException("Analytics planning is not configured.");
        }

        var codes = new AnalyticsPlanContract.Codebook(request.Vocabulary);
        var prompt = AnalyticsPlanContract.BuildPrompt(request, codes);
        var schema = AnalyticsPlanContract.BuildSchema(codes);

        OpenAiResponsesResult response;
        try
        {
            var transport = _testTransport ?? _productionTransport.Value;
            response = await transport.CreateAsync(
                new OpenAiTextRequest(
                    prompt, schema, _options.OpenAiModel, _options.OpenAiReasoningEffort, _options.OpenAiMaximumOutputTokens,
                    SchemaName, SchemaDescription),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is OpenAiServiceException or HttpRequestException or TaskCanceledException)
        {
            throw new AnalyticsPlanningException("OpenAI analytics planning request failed.", exception);
        }
        catch (Exception exception)
        {
            // Anything else from the SDK or client setup is still a planning
            // failure, so the fallback planner can answer; the message names
            // only the exception type (log-safe: no question, no key).
            throw new AnalyticsPlanningException(
                $"OpenAI did not answer (unexpected:{exception.GetType().Name}).", exception);
        }

        if (!string.IsNullOrWhiteSpace(response.Refusal))
        {
            throw new AnalyticsPlanningException("OpenAI refused to plan the question.");
        }

        if (!string.Equals(response.Status, "completed", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(response.Json))
        {
            throw new AnalyticsPlanningException(
                $"OpenAI did not complete the plan (status: {response.Status ?? "unknown"}).");
        }

        return AnalyticsPlanContract.ToResult(response.Json, codes, request, response.InputTokens, response.OutputTokens);
    }

    public void Dispose()
    {
        if (_productionTransport.IsValueCreated)
        {
            _productionTransport.Value.Dispose();
        }
    }
}
