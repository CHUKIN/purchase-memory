using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace PurchaseMemory.Application.Analytics;

/// <summary>The answer card: the plan as the server understood it, what it dropped, and the computed result.</summary>
public sealed record AnalyticsAnswer(
    AnalyticsPlan Plan,
    IReadOnlyList<AnalyticsNote> Notes,
    string Conclusion,
    AnalyticsResult Result,
    DateOnly Today);

/// <summary>A model-planned answer and the period as the model named it (see <see cref="AnalyticsPlanningResult.Period"/>).</summary>
public sealed record AnalyticsPlannedAnswer(AnalyticsAnswer Answer, AnalyticsPeriodRequest? Period);

public sealed record AnalyticsSettings(TimeZoneInfo TimeZone, int MaximumPurchases = 5_000);

public interface IAnalyticsService
{
    bool CanAsk { get; }

    DateOnly Today { get; }

    Task<AnalyticsVocabulary> GetVocabularyAsync(CancellationToken cancellationToken);

    /// <summary>Plans with the model, then runs the plan. Throws <see cref="AnalyticsPlanningException"/>.</summary>
    Task<AnalyticsPlannedAnswer> AskAsync(string question, AnalyticsPlan? previousPlan, string? locale, CancellationToken cancellationToken);

    /// <summary>Runs a plan a person edited (or an MCP client wrote) without any model call.</summary>
    Task<AnalyticsAnswer> RunAsync(AnalyticsPlan plan, string? locale, CancellationToken cancellationToken);
}

public sealed partial class AnalyticsService : IAnalyticsService
{
    public const int MaximumQuestionLength = 500;

    private static readonly JsonSerializerOptions LogJson = new(JsonSerializerDefaults.Web);

    private readonly IAnalyticsRepository _repository;
    private readonly IAnalyticsQueryPlanner _planner;
    private readonly TimeProvider _timeProvider;
    private readonly AnalyticsSettings _settings;
    private readonly ILogger<AnalyticsService> _logger;

    public AnalyticsService(
        IAnalyticsRepository repository,
        IAnalyticsQueryPlanner planner,
        TimeProvider timeProvider,
        AnalyticsSettings settings,
        ILogger<AnalyticsService> logger)
    {
        _repository = repository;
        _planner = planner;
        _timeProvider = timeProvider;
        _settings = settings;
        _logger = logger;
    }

    public bool CanAsk => _planner.IsConfigured;

    public DateOnly Today => AnalyticsCalculator.LocalDay(_timeProvider.GetUtcNow(), _settings.TimeZone);

    public Task<AnalyticsVocabulary> GetVocabularyAsync(CancellationToken cancellationToken) =>
        _repository.GetVocabularyAsync(cancellationToken);

    public async Task<AnalyticsPlannedAnswer> AskAsync(
        string question,
        AnalyticsPlan? previousPlan,
        string? locale,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        if (question.Length > MaximumQuestionLength)
        {
            throw new ArgumentException($"A question cannot exceed {MaximumQuestionLength} characters.", nameof(question));
        }

        if (!_planner.IsConfigured)
        {
            throw new AnalyticsPlanningException("Analytics questions are switched off or not configured.");
        }

        var today = Today;
        var vocabulary = await _repository.GetVocabularyAsync(cancellationToken);
        AnalyticsPlan? previous = null;
        if (previousPlan is not null)
        {
            previousPlan = AnalyticsPlanValidator.CapInput(previousPlan);
            // The client's copy of the previous plan is input like any other.
            var existing = await _repository.FindExistingProductIdsAsync(previousPlan.ProductIds, cancellationToken);
            previous = AnalyticsPlanValidator.Validate(previousPlan, vocabulary, existing, today).Plan;
        }

        var planning = await _planner.PlanAsync(
            new AnalyticsPlanningRequest(question.Trim(), previous, today, vocabulary),
            cancellationToken);
        // The question itself is not logged: it is free text of unknown content.
        // The plan is logged instead — it is what the model understood.
        if (_logger.IsEnabled(LogLevel.Information))
        {
            var planJson = JsonSerializer.Serialize(planning.Plan, LogJson);
            LogPlanned(_logger, question.Length, previous is not null, planning.Understood,
                planJson, planning.InputTokens, planning.OutputTokens);
        }

        if (!planning.Understood)
        {
            throw new AnalyticsQuestionNotUnderstoodException();
        }

        var answer = await ExecuteAsync(planning.Plan, planning.Notes, vocabulary, today, locale, cancellationToken);
        return new AnalyticsPlannedAnswer(answer, planning.Period);
    }

    public async Task<AnalyticsAnswer> RunAsync(AnalyticsPlan plan, string? locale, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var vocabulary = await _repository.GetVocabularyAsync(cancellationToken);
        return await ExecuteAsync(plan, [], vocabulary, Today, locale, cancellationToken);
    }

    private async Task<AnalyticsAnswer> ExecuteAsync(
        AnalyticsPlan plan,
        IReadOnlyList<AnalyticsNote> planningNotes,
        AnalyticsVocabulary vocabulary,
        DateOnly today,
        string? locale,
        CancellationToken cancellationToken)
    {
        plan = AnalyticsPlanValidator.CapInput(plan);
        var existing = await _repository.FindExistingProductIdsAsync(plan.ProductIds, cancellationToken);
        var validated = AnalyticsPlanValidator.Validate(plan, vocabulary, existing, today);
        var valid = validated.Plan;

        // The database narrows by instant with a day of slack on each side; the
        // calculator then applies the exact local calendar days.
        DateTimeOffset? from = valid.From is { } start
            ? new DateTimeOffset(start.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(-1)
            : null;
        DateTimeOffset? to = valid.To is { } end
            ? new DateTimeOffset(end.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(2)
            : null;
        var purchases = await _repository.LoadPurchasesAsync(from, to, _settings.MaximumPurchases + 1, cancellationToken);
        var truncated = purchases.Count > _settings.MaximumPurchases;
        if (truncated)
        {
            purchases = purchases.Take(_settings.MaximumPurchases).ToList();
        }

        var result = AnalyticsCalculator.Calculate(valid, purchases, vocabulary, _settings.TimeZone, truncated);
        if (_logger.IsEnabled(LogLevel.Information))
        {
            LogExecuted(_logger, valid.Metric, valid.GroupBy, result.Coverage.MatchedLines,
                result.Coverage.PurchasesInScope, validated.Notes.Count);
        }

        return new AnalyticsAnswer(
            valid,
            AnalyticsPlanValidator.DedupeNotes(planningNotes.Concat(validated.Notes)),
            AnalyticsAnswerText.Conclusion(result, locale, TagNames(valid, vocabulary)),
            result,
            today);
    }

    private static List<string> TagNames(AnalyticsPlan plan, AnalyticsVocabulary vocabulary)
    {
        var names = vocabulary.Tags.ToDictionary(tag => tag.Id, tag => tag.Name);
        return plan.TagIds.Select(id => names.GetValueOrDefault(id)).OfType<string>().ToList();
    }

    [LoggerMessage(EventId = 7001, Level = LogLevel.Information,
        Message = "Analytics question planned (question length {QuestionLength}, follow-up {FollowUp}, understood {Understood}): {Plan}; tokens in {InputTokens} out {OutputTokens}")]
    private static partial void LogPlanned(ILogger logger, int questionLength, bool followUp, bool understood, string plan, long? inputTokens, long? outputTokens);

    [LoggerMessage(EventId = 7002, Level = LogLevel.Information,
        Message = "Analytics plan executed: {Metric} by {GroupBy}, {MatchedLines} matching lines in {Purchases} receipts, {NoteCount} validation notes")]
    private static partial void LogExecuted(ILogger logger, AnalyticsMetric metric, AnalyticsGroupBy groupBy, int matchedLines, int purchases, int noteCount);
}
