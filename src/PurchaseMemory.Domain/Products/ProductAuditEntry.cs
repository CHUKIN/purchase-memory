namespace PurchaseMemory.Domain.Products;

/// <summary>
/// Append-only record of a change to a product's category, brand or tags. It mirrors
/// the receipt review audit trail, but belongs to the product: a category is a
/// property of the product, not of any one receipt.
/// </summary>
public sealed class ProductAuditEntry
{
    public const string CategoryField = "category";
    public const string BrandField = "brand";
    public const string TagsField = "tags";
    public const int MaximumValueLength = 500;

    private ProductAuditEntry()
    {
        // Required by Entity Framework Core.
    }

    private ProductAuditEntry(
        Guid productId,
        string actor,
        string? actorEmail,
        string fieldName,
        ProductAuditChangeSource source,
        string? oldValue,
        string? newValue,
        DateTimeOffset occurredAt)
    {
        Id = Guid.NewGuid();
        ProductId = productId;
        Actor = Limit(actor.Trim(), 200);
        ActorEmail = actorEmail is null ? null : Limit(actorEmail.Trim(), 320);
        FieldName = fieldName;
        Source = source;
        OldValue = oldValue is null ? null : Limit(oldValue, MaximumValueLength);
        NewValue = newValue is null ? null : Limit(newValue, MaximumValueLength);
        OccurredAt = occurredAt;
    }

    public Guid Id { get; private set; }

    public Guid ProductId { get; private set; }

    /// <summary>User id for human changes, <c>ai:&lt;model&gt;</c> for suggestions, <c>system</c> otherwise.</summary>
    public string Actor { get; private set; } = string.Empty;

    public string? ActorEmail { get; private set; }

    public string FieldName { get; private set; } = string.Empty;

    public ProductAuditChangeSource Source { get; private set; }

    public string? OldValue { get; private set; }

    public string? NewValue { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public static ProductAuditEntry Create(
        Guid productId,
        string actor,
        string? actorEmail,
        string fieldName,
        ProductAuditChangeSource source,
        string? oldValue,
        string? newValue,
        DateTimeOffset occurredAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);
        return new ProductAuditEntry(productId, actor, actorEmail, fieldName, source, oldValue, newValue, occurredAt);
    }

    private static string Limit(string value, int length) => value.Length <= length ? value : value[..length];
}

public enum ProductAuditChangeSource
{
    Human = 0,
    Suggestion = 1,

    /// <summary>The product's category was deleted and the product moved.</summary>
    CategoryDeleted = 2,

    /// <summary>A tag the product carried was deleted, so the product lost it.</summary>
    TagDeleted = 3
}
