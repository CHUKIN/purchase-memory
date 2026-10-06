namespace PurchaseMemory.Application.Analytics;

/// <summary>
/// Turns a natural-language question into a query plan. Implementations talk
/// to a model, but the model only translates: it never sees purchases, never
/// writes SQL and never computes a number. Whatever comes back is validated by
/// <see cref="AnalyticsPlanValidator"/> before it is executed.
/// </summary>
public interface IAnalyticsQueryPlanner
{
    /// <summary>Whether a call can be made at all (switched on, model and credential configured).</summary>
    bool IsConfigured { get; }

    string ModelName { get; }

    Task<AnalyticsPlanningResult> PlanAsync(AnalyticsPlanningRequest request, CancellationToken cancellationToken);
}

/// <param name="Question">The user's question, in any language.</param>
/// <param name="PreviousPlan">The validated plan of the question being followed up, if any.</param>
/// <param name="Today">The household's local date; relative periods are resolved against it.</param>
public sealed record AnalyticsPlanningRequest(
    string Question,
    AnalyticsPlan? PreviousPlan,
    DateOnly Today,
    AnalyticsVocabulary Vocabulary);

/// <param name="Plan">Unvalidated: ids and names are what the model returned, mapped back from codes.</param>
/// <param name="Understood">False when the model said the question is not about purchases.</param>
public sealed record AnalyticsPlanningResult(
    AnalyticsPlan Plan,
    bool Understood,
    IReadOnlyList<AnalyticsNote> Notes,
    long? InputTokens,
    long? OutputTokens);

/// <summary>The model could not be asked or answered with something unusable.</summary>
public class AnalyticsPlanningException : Exception
{
    public AnalyticsPlanningException()
    {
    }

    public AnalyticsPlanningException(string message)
        : base(message)
    {
    }

    public AnalyticsPlanningException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>The model answered, but the question is not about the household's purchases.</summary>
public sealed class AnalyticsQuestionNotUnderstoodException : AnalyticsPlanningException
{
    public AnalyticsQuestionNotUnderstoodException()
        : base("The question is not about purchases.")
    {
    }

    public AnalyticsQuestionNotUnderstoodException(string message)
        : base(message)
    {
    }

    public AnalyticsQuestionNotUnderstoodException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
