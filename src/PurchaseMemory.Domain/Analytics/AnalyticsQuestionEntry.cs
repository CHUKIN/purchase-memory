using System.Text;

namespace PurchaseMemory.Domain.Analytics;

/// <summary>
/// One question in the household's analytics history: what was asked, the
/// plan the model turned it into and the last answer computed from that plan.
/// Asking the same words again reuses the saved plan instead of the model, and
/// only refreshes the answer and the time.
/// </summary>
/// <remarks>
/// The plan, the named period and the answer are stored as JSON written by the
/// application layer; the domain only keeps them together and in order. A
/// follow-up question («а по магазинам?») depends on the answer it refined, so
/// its words alone never select a saved plan for reuse.
/// </remarks>
public sealed class AnalyticsQuestionEntry
{
    public const int MaximumQuestionLength = 500;
    public const int MaximumConclusionLength = 1000;
    public const int MaximumUserIdLength = 200;
    public const int MaximumEmailLength = 320;

    private AnalyticsQuestionEntry()
    {
        // Required by Entity Framework Core.
    }

    public Guid Id { get; private set; }

    /// <summary>The words as last asked, trimmed.</summary>
    public string Question { get; private set; } = string.Empty;

    /// <summary>The lookup key: see <see cref="Normalize"/>.</summary>
    public string NormalizedQuestion { get; private set; } = string.Empty;

    /// <summary>Asked as a refinement of a previous answer.</summary>
    public bool IsFollowUp { get; private set; }

    /// <summary>The validated plan, as JSON.</summary>
    public string PlanJson { get; private set; } = string.Empty;

    /// <summary>
    /// The period as the model named it («this month», «August») as JSON, so a
    /// repeat counts it from the new «today»; null when the dates are fixed.
    /// </summary>
    public string? PeriodJson { get; private set; }

    /// <summary>The last answer exactly as the page shows it, as JSON.</summary>
    public string AnswerJson { get; private set; } = string.Empty;

    /// <summary>A copy of the answer's conclusion for the history list.</summary>
    public string Conclusion { get; private set; } = string.Empty;

    /// <summary>Supabase user id (<c>sub</c>) of whoever asked it last.</summary>
    public string AskedByUserId { get; private set; } = string.Empty;

    public string? AskedByEmail { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When the stored answer was computed; the history is ordered by it.</summary>
    public DateTimeOffset AnsweredAt { get; private set; }

    public static AnalyticsQuestionEntry Create(
        Guid id,
        string question,
        bool isFollowUp,
        string planJson,
        string? periodJson,
        string answerJson,
        string conclusion,
        string askedByUserId,
        string? askedByEmail,
        DateTimeOffset at)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("An entry identifier cannot be empty.", nameof(id));
        }

        var entry = new AnalyticsQuestionEntry { Id = id, IsFollowUp = isFollowUp, CreatedAt = at };
        entry.Ask(question, planJson, periodJson, answerJson, conclusion, askedByUserId, askedByEmail, at);
        return entry;
    }

    /// <summary>The question was asked again (from the saved plan or the model): new words, asker and answer.</summary>
    public void Ask(
        string question,
        string planJson,
        string? periodJson,
        string answerJson,
        string conclusion,
        string askedByUserId,
        string? askedByEmail,
        DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        ArgumentException.ThrowIfNullOrWhiteSpace(askedByUserId);
        var trimmed = question.Trim();
        if (trimmed.Length > MaximumQuestionLength)
        {
            throw new ArgumentException($"A question cannot exceed {MaximumQuestionLength} characters.", nameof(question));
        }

        Question = trimmed;
        NormalizedQuestion = Normalize(trimmed);
        PlanJson = planJson;
        PeriodJson = periodJson;
        AskedByUserId = Limit(askedByUserId.Trim(), MaximumUserIdLength);
        AskedByEmail = string.IsNullOrWhiteSpace(askedByEmail) ? null : Limit(askedByEmail.Trim(), MaximumEmailLength);
        Answer(planJson, answerJson, conclusion, at);
    }

    /// <summary>The saved plan was recalculated on fresh data («Обновить»); the asker stays.</summary>
    public void Answer(string planJson, string answerJson, string conclusion, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(answerJson);
        PlanJson = planJson;
        AnswerJson = answerJson;
        Conclusion = Limit(conclusion?.Trim() ?? string.Empty, MaximumConclusionLength);
        AnsweredAt = at;
    }

    /// <summary>
    /// Same words, same key: trimmed, lower-cased (invariant) and with every run
    /// of whitespace collapsed to one space. Punctuation is kept on purpose —
    /// «молоко?» and «молоко» stay the same question only if typed the same way.
    /// </summary>
    public static string Normalize(string question)
    {
        ArgumentNullException.ThrowIfNull(question);
        var builder = new StringBuilder(question.Length);
        var pendingSpace = false;
        foreach (var character in question.Trim())
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = true;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }

    private static string Limit(string value, int length) => value.Length <= length ? value : value[..length];
}
