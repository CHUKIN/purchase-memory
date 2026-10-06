namespace PurchaseMemory.Domain.Receipts;

/// <summary>
/// One append-only record of a human correction made during receipt review.
/// <see cref="Actor"/> is the signed-in user's stable id (Supabase <c>sub</c>);
/// entries written before sign-in existed keep the placeholder "Owner".
/// <see cref="ActorEmail"/> is a readable copy captured at the time of change.
/// </summary>
public sealed class ReceiptReviewAuditEntry
{
    private ReceiptReviewAuditEntry()
    {
        // Required by Entity Framework Core.
    }

    private ReceiptReviewAuditEntry(
        Guid id,
        string actor,
        string? actorEmail,
        string fieldName,
        ReceiptReviewAuditChangeType changeType,
        string? oldValue,
        string? newValue,
        DateTimeOffset occurredAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Audit entry identifier cannot be empty.", nameof(id));
        }

        Id = id;
        Actor = NormalizeRequiredText(actor, nameof(actor), 200);
        ActorEmail = NormalizeOptionalText(actorEmail?.Trim(), 320);
        FieldName = NormalizeRequiredText(fieldName, nameof(fieldName), 200);
        ChangeType = changeType;
        OldValue = NormalizeOptionalText(oldValue, 2000);
        NewValue = NormalizeOptionalText(newValue, 2000);
        OccurredAt = occurredAt;
    }

    public Guid Id { get; private set; }

    public string Actor { get; private set; } = string.Empty;

    public string? ActorEmail { get; private set; }

    public string FieldName { get; private set; } = string.Empty;

    public ReceiptReviewAuditChangeType ChangeType { get; private set; }

    public string? OldValue { get; private set; }

    public string? NewValue { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public static ReceiptReviewAuditEntry Create(
        string actor,
        string? actorEmail,
        string fieldName,
        ReceiptReviewAuditChangeType changeType,
        string? oldValue,
        string? newValue,
        DateTimeOffset occurredAt)
    {
        return new ReceiptReviewAuditEntry(
            Guid.NewGuid(),
            actor,
            string.IsNullOrWhiteSpace(actorEmail) ? null : actorEmail,
            fieldName,
            changeType,
            oldValue,
            newValue,
            occurredAt);
    }

    private static string NormalizeRequiredText(string value, string parameterName, int maximumLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException($"Value cannot exceed {maximumLength} characters.", parameterName);
        }

        return normalized;
    }

    private static string? NormalizeOptionalText(string? value, int maximumLength)
    {
        if (value is null)
        {
            return null;
        }

        return value.Length > maximumLength ? value[..maximumLength] : value;
    }
}
