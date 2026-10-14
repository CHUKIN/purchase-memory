using Microsoft.Extensions.Logging;
using PurchaseMemory.Application.Analytics;

namespace PurchaseMemory.Infrastructure.Analytics;

/// <summary>
/// The planner the application sees: asks the configured planners in order
/// (<c>Analytics:Provider</c>, then <c>Analytics:FallbackProviders</c>) and
/// returns the first usable plan. A planner without a key is skipped; a
/// planner that fails (<see cref="AnalyticsPlanningException"/>) hands the
/// question to the next one. «Not about purchases» is an answer (a result with
/// <c>Understood = false</c>), not a failure, so it is never retried elsewhere.
/// </summary>
public sealed partial class AnalyticsQueryPlannerRouter : IAnalyticsQueryPlanner
{
    private readonly IReadOnlyList<(string Provider, IAnalyticsQueryPlanner Planner)> _planners;
    private readonly ILogger<AnalyticsQueryPlannerRouter> _logger;

    public AnalyticsQueryPlannerRouter(
        IReadOnlyList<(string Provider, IAnalyticsQueryPlanner Planner)> planners,
        ILogger<AnalyticsQueryPlannerRouter> logger)
    {
        ArgumentNullException.ThrowIfNull(planners);
        _planners = planners;
        _logger = logger;
    }

    public bool IsConfigured => _planners.Any(entry => entry.Planner.IsConfigured);

    /// <summary>The model that answers when nothing fails.</summary>
    public string ModelName =>
        _planners.FirstOrDefault(entry => entry.Planner.IsConfigured).Planner?.ModelName
        ?? (_planners.Count > 0 ? _planners[0].Planner.ModelName : string.Empty);

    public async Task<AnalyticsPlanningResult> PlanAsync(AnalyticsPlanningRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        AnalyticsPlanningException? lastFailure = null;
        foreach (var (provider, planner) in _planners)
        {
            if (!planner.IsConfigured)
            {
                continue;
            }

            try
            {
                var result = await planner.PlanAsync(request, cancellationToken);
                if (lastFailure is not null)
                {
                    FallbackAnswered(provider);
                }

                return result;
            }
            catch (AnalyticsPlanningException exception)
            {
                // The message is log-safe (a failure category, no question text).
                PlannerFailed(provider, exception.Message);
                lastFailure = exception;
            }
        }

        throw lastFailure ?? new AnalyticsPlanningException("Analytics questions are switched off or not configured.");
    }

    [LoggerMessage(
        EventId = 3423,
        Level = LogLevel.Warning,
        Message = "Analytics planner {Provider} failed: {Reason}; trying the next one if configured")]
    private partial void PlannerFailed(string provider, string reason);

    [LoggerMessage(
        EventId = 3424,
        Level = LogLevel.Information,
        Message = "Analytics question planned by fallback planner {Provider}")]
    private partial void FallbackAnswered(string provider);
}
