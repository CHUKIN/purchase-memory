namespace PurchaseMemory.Domain.Impressions;

/// <summary>The confirmed three-level scale: не нравится / средне / нравится.</summary>
public enum ImpressionRating
{
    Dislike = 0,
    Neutral = 1,
    Like = 2
}

/// <summary>
/// One person's opinion about one purchased line, at one moment. Impressions
/// are an append-only history: a changed opinion is a new impression, never an
/// overwrite. The only change after creation is the author withdrawing their
/// own impression (soft delete), so a mistaken tap can be undone while the row
/// itself is kept.
/// </summary>
/// <remarks>
/// <see cref="PurchaseItemId"/> is the link to the line and, through it, to the
/// product, store, date and price. Editing a purchase replaces its line rows;
/// the repository then moves the impression to the replacement line with the
/// same name. When no such line is left the link becomes null and the purchase,
/// the line name and the product id captured at creation still place the
/// impression in history.
/// </remarks>
public sealed class ProductImpression
{
    public const int MaximumNoteLength = 2000;
    public const int MaximumItemNameLength = 500;

    private ProductImpression()
    {
        // Required by Entity Framework Core.
    }

    private ProductImpression(
        Guid id,
        Guid purchaseId,
        Guid purchaseItemId,
        Guid? productId,
        string itemName,
        string authorUserId,
        string? authorEmail,
        ImpressionRating rating,
        string? note,
        string? photoStorageKey,
        string? photoContentType,
        DateTimeOffset createdAt)
    {
        Id = id;
        PurchaseId = purchaseId;
        PurchaseItemId = purchaseItemId;
        ProductId = productId;
        ItemName = Limit(itemName.Trim(), MaximumItemNameLength);
        AuthorUserId = authorUserId.Trim();
        AuthorEmail = string.IsNullOrWhiteSpace(authorEmail) ? null : Limit(authorEmail.Trim(), 320);
        Rating = rating;
        Note = note;
        PhotoStorageKey = photoStorageKey;
        PhotoContentType = photoContentType;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid PurchaseId { get; private set; }

    /// <summary>Null only after the line was removed by a purchase correction.</summary>
    public Guid? PurchaseItemId { get; private set; }

    /// <summary>The product the line was linked to when the impression was made.</summary>
    public Guid? ProductId { get; private set; }

    /// <summary>The receipt line as it read when rated; survives the line being removed.</summary>
    public string ItemName { get; private set; } = string.Empty;

    /// <summary>Supabase user id (<c>sub</c>) of the household member.</summary>
    public string AuthorUserId { get; private set; } = string.Empty;

    public string? AuthorEmail { get; private set; }

    public ImpressionRating Rating { get; private set; }

    public string? Note { get; private set; }

    /// <summary>Storage key of the impression photo (not a product photo).</summary>
    public string? PhotoStorageKey { get; private set; }

    public string? PhotoContentType { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public bool IsDeleted => DeletedAt is not null;

    public static ProductImpression Create(
        Guid id,
        Guid purchaseId,
        Guid purchaseItemId,
        Guid? productId,
        string itemName,
        string authorUserId,
        string? authorEmail,
        ImpressionRating rating,
        string? note,
        string? photoStorageKey,
        string? photoContentType,
        DateTimeOffset createdAt)
    {
        if (id == Guid.Empty || purchaseId == Guid.Empty || purchaseItemId == Guid.Empty)
        {
            throw new ArgumentException("Impression, purchase and line identifiers cannot be empty.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(itemName);
        ArgumentException.ThrowIfNullOrWhiteSpace(authorUserId);
        if (!Enum.IsDefined(rating))
        {
            throw new ArgumentException("Rating must be dislike, neutral or like.", nameof(rating));
        }

        if ((photoStorageKey is null) != (photoContentType is null))
        {
            throw new ArgumentException("A photo needs both a storage key and a content type.");
        }

        return new ProductImpression(
            id,
            purchaseId,
            purchaseItemId,
            productId,
            itemName,
            authorUserId,
            authorEmail,
            rating,
            NormalizeNote(note),
            photoStorageKey,
            photoContentType,
            createdAt);
    }

    public static string? NormalizeNote(string? note)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return null;
        }

        var normalized = note.Trim();
        if (normalized.Length > MaximumNoteLength)
        {
            throw new ArgumentException($"A note cannot exceed {MaximumNoteLength} characters.", nameof(note));
        }

        return normalized;
    }

    public bool IsAuthoredBy(string userId) => string.Equals(AuthorUserId, userId, StringComparison.Ordinal);

    public void Delete(DateTimeOffset deletedAt)
    {
        DeletedAt ??= deletedAt;
    }

    /// <summary>Follows the line through a purchase correction; null when it is gone.</summary>
    public void MoveToItem(Guid? purchaseItemId)
    {
        PurchaseItemId = purchaseItemId == Guid.Empty ? null : purchaseItemId;
    }

    private static string Limit(string value, int length) => value.Length <= length ? value : value[..length];
}
