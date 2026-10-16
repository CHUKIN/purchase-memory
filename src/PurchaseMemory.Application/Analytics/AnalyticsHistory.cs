using System.Text.Json;
using Microsoft.Extensions.Logging;
using PurchaseMemory.Application.Receipts;
using PurchaseMemory.Domain.Analytics;

namespace PurchaseMemory.Application.Analytics;

/// <summary>
/// The answer to an asked question, plus where it lives in the history.
/// The first five fields are exactly <see cref="AnalyticsAnswer"/>, so the
/// page renders it the same way.
/// </summary>
/// <param name="HistoryId">The history entry it was saved to; null if saving failed (the answer still stands).</param>
/// <param name="Reused">The saved plan of an earlier identical question was run; no model was called.</param>
public sealed record AnalyticsAskOutcome(
    AnalyticsPlan Plan,
    IReadOnlyList<AnalyticsNote> Notes,
    string Conclusion,
    AnalyticsResult Result,
    DateOnly Today,
    Guid? HistoryId,
    bool Reused)
{
    public static AnalyticsAskOutcome From(AnalyticsAnswer answer, Guid? historyId, bool reused) =>
        new(answer.Plan, answer.Notes, answer.Conclusion, answer.Result, answer.Today, historyId, reused);
}

/// <summary>One line of «Предыдущие вопросы». The asker's id stays on the server; the page only learns whether it was the caller.</summary>
public sealed record AnalyticsHistoryItem(
    Guid Id,
    string Question,
    bool IsFollowUp,
    string Conclusion,
    string? AskedByEmail,
    bool AskedByMe,
    DateTimeOffset CreatedAt,
    DateTimeOffset AnsweredAt);

public sealed record AnalyticsHistoryPage(IReadOnlyList<AnalyticsHistoryItem> Items, bool HasMore);

/// <param name="Answer">The saved answer; null only if it was written by an incompatible older version.</param>
public sealed record AnalyticsHistoryDetail(AnalyticsHistoryItem Entry, AnalyticsAnswer? Answer);

/// <summary>A history row without its JSON payloads, for the list.</summary>
public sealed record AnalyticsHistoryRow(
    Guid Id,
    string Question,
    bool IsFollowUp,
    string Conclusion,
    string AskedByUserId,
    string? AskedByEmail,
    DateTimeOffset CreatedAt,
    DateTimeOffset AnsweredAt);

public interface IAnalyticsHistoryRepository
{
    Task<AnalyticsQuestionEntry?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// The most recently answered entry with this key. For a follow-up the
    /// resulting plan must match as well, because the words alone depend on
    /// the answer they refined.
    /// </summary>
    Task<AnalyticsQuestionEntry?> FindLatestAsync(
        string normalizedQuestion,
        bool isFollowUp,
        string? planJson,
        CancellationToken cancellationToken);

    /// <summary>Newest answer first; <c>HasMore</c> tells whether a further page exists.</summary>
    Task<(IReadOnlyList<AnalyticsHistoryRow> Rows, bool HasMore)> ListAsync(int offset, int limit, CancellationToken cancellationToken);

    void Add(AnalyticsQuestionEntry entry);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Removes everything but the <paramref name="keep"/> most recently answered entries.</summary>
    Task TrimAsync(int keep, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>The model cannot be asked here (switched off or not configured) and no saved plan matches.</summary>
public sealed class AnalyticsQuestionsDisabledException : AnalyticsPlanningException
{
    public AnalyticsQuestionsDisabledException()
        : base("Analytics questions are switched off or not configured.")
    {
    }

    public AnalyticsQuestionsDisabledException(string message)
        : base(message)
    {
    }

    public AnalyticsQuestionsDisabledException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// A history entry whose saved plan cannot be read any more (written by an
/// incompatible older version): it can be viewed in the list and deleted, but
/// not recalculated. Asking the same words again plans it afresh.
/// </summary>
public sealed class AnalyticsHistoryEntryOutdatedException : Exception
{
    public AnalyticsHistoryEntryOutdatedException()
        : base("The saved plan of this analytics history entry cannot be read.")
    {
    }

    public AnalyticsHistoryEntryOutdatedException(Guid entryId)
        : base($"The saved plan of analytics history entry {entryId} cannot be read.")
    {
    }

    public AnalyticsHistoryEntryOutdatedException(string message)
        : base(message)
    {
    }

    public AnalyticsHistoryEntryOutdatedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Per-user allowance for questions that really call the model. A question
/// answered from the history never asks for it.
/// </summary>
public interface IAnalyticsModelQuota
{
    /// <summary>Takes one model call from the user's allowance; null when allowed, else how long to wait.</summary>
    TimeSpan? TryAcquire(string userId);
}

/// <summary>The user has asked the model too many questions for now.</summary>
public sealed class AnalyticsRateLimitedException : Exception
{
    public AnalyticsRateLimitedException()
        : base("Too many analytics questions in a short time.")
    {
    }

    public AnalyticsRateLimitedException(string message)
        : base(message)
    {
    }

    public AnalyticsRateLimitedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public AnalyticsRateLimitedException(TimeSpan retryAfter)
        : this()
    {
        RetryAfter = retryAfter;
    }

    public TimeSpan? RetryAfter { get; }
}

public interface IAnalyticsHistoryService
{
    /// <summary>
    /// Answers a question and records it. A standalone question asked before
    /// runs its saved plan without the model unless <paramref name="askModel"/>
    /// is set («Спросить заново»); either way the existing entry is updated
    /// instead of duplicated.
    /// </summary>
    /// <exception cref="AnalyticsRateLimitedException">The model would be called but the user's allowance is used up.</exception>
    Task<AnalyticsAskOutcome> AskAsync(
        string question,
        AnalyticsPlan? previousPlan,
        string? locale,
        bool askModel,
        ReceiptActor actor,
        CancellationToken cancellationToken);

    Task<AnalyticsHistoryPage> ListAsync(int offset, int limit, ReceiptActor actor, CancellationToken cancellationToken);

    Task<AnalyticsHistoryDetail?> GetAsync(Guid id, ReceiptActor actor, CancellationToken cancellationToken);

    /// <summary>Recalculates the saved plan on today's data, without a model call («Обновить»).</summary>
    /// <exception cref="AnalyticsHistoryEntryOutdatedException">The saved plan was written by an incompatible older version.</exception>
    Task<AnalyticsHistoryDetail?> RefreshAsync(Guid id, string? locale, ReceiptActor actor, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>
/// The household's analytics history. Data in this version is shared by every
/// approved user, so the history is too: anyone in the household sees, reruns
/// and deletes any entry, and each entry shows who asked it last.
/// </summary>
public sealed partial class AnalyticsHistoryService : IAnalyticsHistoryService
{
    public const int DefaultPageSize = 20;
    public const int MaximumPageSize = 100;

    /// <summary>Older entries are dropped beyond this; a two-person household never scrolls that far.</summary>
    public const int MaximumEntries = 300;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IAnalyticsService _analytics;
    private readonly IAnalyticsHistoryRepository _repository;
    private readonly IAnalyticsModelQuota _quota;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AnalyticsHistoryService> _logger;

    public AnalyticsHistoryService(
        IAnalyticsService analytics,
        IAnalyticsHistoryRepository repository,
        IAnalyticsModelQuota quota,
        TimeProvider timeProvider,
        ILogger<AnalyticsHistoryService> logger)
    {
        _analytics = analytics;
        _repository = repository;
        _quota = quota;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<AnalyticsAskOutcome> AskAsync(
        string question,
        AnalyticsPlan? previousPlan,
        string? locale,
        bool askModel,
        ReceiptActor actor,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        ArgumentNullException.ThrowIfNull(actor);
        var normalized = AnalyticsQuestionEntry.Normalize(question);
        var followUp = previousPlan is not null;

        if (!followUp && !askModel)
        {
            var saved = await _repository.FindLatestAsync(normalized, isFollowUp: false, planJson: null, cancellationToken);
            if (saved is not null && ReadPlan(saved) is { } savedPlan)
            {
                var repeated = await _analytics.RunAsync(Recount(savedPlan, saved.PeriodJson), locale, cancellationToken);
                LogReused(_logger, saved.Id);
                try
                {
                    saved.Ask(question, Serialize(repeated.Plan), saved.PeriodJson, Serialize(repeated), repeated.Conclusion,
                        actor.UserId, actor.Email, _timeProvider.GetUtcNow());
                    await _repository.SaveChangesAsync(cancellationToken);
                    return AnalyticsAskOutcome.From(repeated, saved.Id, reused: true);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // Same rule as for a new answer: the computed answer still reaches the person.
                    LogSaveFailed(_logger, exception);
                    return AnalyticsAskOutcome.From(repeated, historyId: null, reused: true);
                }
            }
        }

        if (!_analytics.CanAsk)
        {
            throw new AnalyticsQuestionsDisabledException();
        }

        // Only a question that really calls the model uses the allowance.
        if (_quota.TryAcquire(actor.UserId) is { } retryAfter)
        {
            throw new AnalyticsRateLimitedException(retryAfter);
        }

        var planned = await _analytics.AskAsync(question, previousPlan, locale, cancellationToken);
        var historyId = await SaveAsync(question, normalized, followUp, planned, actor, cancellationToken);
        return AnalyticsAskOutcome.From(planned.Answer, historyId, reused: false);
    }

    public async Task<AnalyticsHistoryPage> ListAsync(int offset, int limit, ReceiptActor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var (rows, hasMore) = await _repository.ListAsync(
            Math.Max(0, offset),
            Math.Clamp(limit, 1, MaximumPageSize),
            cancellationToken);
        return new AnalyticsHistoryPage(rows.Select(row => ToItem(row, actor)).ToList(), hasMore);
    }

    public async Task<AnalyticsHistoryDetail?> GetAsync(Guid id, ReceiptActor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var entry = await _repository.FindAsync(id, cancellationToken);
        return entry is null ? null : new AnalyticsHistoryDetail(ToItem(entry, actor), ReadAnswer(entry));
    }

    public async Task<AnalyticsHistoryDetail?> RefreshAsync(Guid id, string? locale, ReceiptActor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var entry = await _repository.FindAsync(id, cancellationToken);
        if (entry is null)
        {
            return null;
        }

        var plan = ReadPlan(entry) ?? throw new AnalyticsHistoryEntryOutdatedException(id);
        var answer = await _analytics.RunAsync(Recount(plan, entry.PeriodJson), locale, cancellationToken);
        entry.Answer(Serialize(answer.Plan), Serialize(answer), answer.Conclusion, _timeProvider.GetUtcNow());
        await _repository.SaveChangesAsync(cancellationToken);
        return new AnalyticsHistoryDetail(ToItem(entry, actor), answer);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        _repository.DeleteAsync(id, cancellationToken);

    /// <summary>
    /// A paid answer must reach the person even if the history cannot be
    /// written, so a failure here is logged and the answer goes out without an id.
    /// </summary>
    private async Task<Guid?> SaveAsync(
        string question,
        string normalized,
        bool followUp,
        AnalyticsPlannedAnswer planned,
        ReceiptActor actor,
        CancellationToken cancellationToken)
    {
        var answer = planned.Answer;
        var planJson = Serialize(answer.Plan);
        var periodJson = planned.Period is null ? null : JsonSerializer.Serialize(planned.Period, Json);
        var now = _timeProvider.GetUtcNow();
        try
        {
            var entry = await _repository.FindLatestAsync(normalized, followUp, followUp ? planJson : null, cancellationToken);
            if (entry is null)
            {
                entry = AnalyticsQuestionEntry.Create(
                    Guid.NewGuid(), question, followUp, planJson, periodJson, Serialize(answer), answer.Conclusion,
                    actor.UserId, actor.Email, now);
                _repository.Add(entry);
            }
            else
            {
                entry.Ask(question, planJson, periodJson, Serialize(answer), answer.Conclusion, actor.UserId, actor.Email, now);
            }

            await _repository.SaveChangesAsync(cancellationToken);
            await _repository.TrimAsync(MaximumEntries, cancellationToken);
            return entry.Id;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogSaveFailed(_logger, exception);
            return null;
        }
    }

    /// <summary>A named period («this month») is counted again from today; fixed dates stay as they were.</summary>
    private AnalyticsPlan Recount(AnalyticsPlan plan, string? periodJson)
    {
        if (periodJson is null)
        {
            return plan;
        }

        AnalyticsPeriodRequest? period;
        try
        {
            period = JsonSerializer.Deserialize<AnalyticsPeriodRequest>(periodJson, Json);
        }
        catch (JsonException)
        {
            return plan;
        }

        if (period is null)
        {
            return plan;
        }

        var resolved = AnalyticsPeriodResolver.Resolve(period, _analytics.Today);
        return resolved.Valid ? plan with { From = resolved.From, To = resolved.To } : plan;
    }

    private static AnalyticsPlan? ReadPlan(AnalyticsQuestionEntry entry) => TryDeserialize<AnalyticsPlan>(entry.PlanJson);

    private static AnalyticsAnswer? ReadAnswer(AnalyticsQuestionEntry entry) => TryDeserialize<AnalyticsAnswer>(entry.AnswerJson);

    private static T? TryDeserialize<T>(string json)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);

    private static AnalyticsHistoryItem ToItem(AnalyticsHistoryRow row, ReceiptActor actor) => new(
        row.Id,
        row.Question,
        row.IsFollowUp,
        row.Conclusion,
        row.AskedByEmail,
        string.Equals(row.AskedByUserId, actor.UserId, StringComparison.Ordinal),
        row.CreatedAt,
        row.AnsweredAt);

    private static AnalyticsHistoryItem ToItem(AnalyticsQuestionEntry entry, ReceiptActor actor) => ToItem(
        new AnalyticsHistoryRow(entry.Id, entry.Question, entry.IsFollowUp, entry.Conclusion, entry.AskedByUserId,
            entry.AskedByEmail, entry.CreatedAt, entry.AnsweredAt),
        actor);

    [LoggerMessage(EventId = 7003, Level = LogLevel.Information,
        Message = "Analytics question repeated: saved plan of history entry {EntryId} reused without a model call")]
    private static partial void LogReused(ILogger logger, Guid entryId);

    [LoggerMessage(EventId = 7004, Level = LogLevel.Warning,
        Message = "Analytics answer could not be saved to the history; it is returned without a history entry")]
    private static partial void LogSaveFailed(ILogger logger, Exception exception);
}
