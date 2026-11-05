namespace PurchaseMemory.Domain.Products;

/// <summary>
/// Append-only record of a change to a product's category, brand, tags, name,
/// «проверить» flag, its creation by hand or a merge. It mirrors
/// the receipt review audit trail, but belongs to the product: a category is a
/// property of the product, not of any one receipt.
/// </summary>
public sealed class ProductAuditEntry
{
    public const string CategoryField = "category";
    public const string BrandField = "brand";
    public const string TagsField = "tags";
    public const string NameField = "name";

    /// <summary>The human Polish name (<see cref="Product.NamePl"/>), not the receipt text.</summary>
    public const string NamePlField = "namePl";

    /// <summary>The human Russian name (<see cref="Product.NameRu"/>).</summary>
    public const string NameRuField = "nameRu";

    /// <summary>The «проверить» flag was raised or cleared.</summary>
    public const string ReviewField = "review";

    /// <summary>A person created the product by hand or from a barcode.</summary>
    public const string CreatedField = "created";

    /// <summary>
    /// Another product was merged into this one. Old value describes the
    /// merged-away product, new value this one, both as «name (id)».
    /// </summary>
    public const string MergeField = "merge";
    /// <summary>A photo was added (new value = photo id) or deleted (old value).</summary>
    public const string PhotoField = "photo";

    /// <summary>The main photo changed; values are photo ids.</summary>
    public const string MainPhotoField = "mainPhoto";

    /// <summary>A barcode was added (new value = «code (source)») or removed (old value = code).</summary>
    public const string BarcodeField = "barcode";

    /// <summary>«Больше не покупать» was set (new value «Avoid») or cleared (old value «Avoid»).</summary>
    public const string AvoidField = "avoid";

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
    TagDeleted = 3,

    /// <summary>The deterministic matcher created the product with the «проверить» flag.</summary>
    Matching = 4
}
