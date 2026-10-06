using PurchaseMemory.Domain.Impressions;
using PurchaseMemory.Domain.Purchases;

namespace PurchaseMemory.Application.Impressions;

/// <summary>An impression with the purchase context it is judged in: store, date and price.</summary>
public sealed record ImpressionView(
    Guid Id,
    Guid PurchaseId,
    Guid? PurchaseItemId,
    Guid? ProductId,
    string ItemName,
    ImpressionRating Rating,
    string? Note,
    bool HasPhoto,
    string AuthorUserId,
    string? AuthorEmail,
    bool IsMine,
    DateTimeOffset CreatedAt,
    string? MerchantName,
    DateTimeOffset? PurchasedAt,
    string? Currency,
    decimal? Quantity,
    string? Unit,
    decimal? UnitPrice,
    decimal? LineTotal);

/// <summary>A bought line that can be rated: recent unrated ones, or a product's purchases.</summary>
public sealed record RateablePurchaseItemView(
    Guid PurchaseItemId,
    Guid PurchaseId,
    Guid? ProductId,
    string? ProductName,
    string ItemName,
    string? MerchantName,
    DateTimeOffset PurchasedAt,
    string? Currency,
    decimal? UnitPrice,
    decimal? LineTotal);

public sealed record ImpressionPhotoUpload(Stream Content, string DeclaredContentType);

public sealed record CreateImpressionCommand(
    Guid PurchaseItemId,
    ImpressionRating Rating,
    string? Note,
    ImpressionPhotoUpload? Photo);

/// <summary>What the repository knows about the line being rated.</summary>
public sealed record ImpressionItemContext(Guid PurchaseItemId, Guid PurchaseId, Guid? ProductId, string Name, bool IsDiscount);

/// <summary>A line row as the relinking sees it: identity, place and text.</summary>
public sealed record ImpressionLineRef(Guid Id, int Position, string Name, bool IsDiscount)
{
    public static ImpressionLineRef From(PurchaseItem item) => new(item.Id, item.Position, item.Name, item.IsDiscount);
}

public sealed record StoredImpressionPhoto(string StorageKey, string ContentType);

public sealed class ImpressionNotFoundException(string message) : Exception(message);

public sealed class ImpressionPurchaseItemNotFoundException(Guid purchaseItemId)
    : Exception($"Purchase line '{purchaseItemId}' was not found.");

/// <summary>Someone tried to change an impression that belongs to another household member.</summary>
public sealed class ImpressionAccessDeniedException()
    : Exception("Only the author can remove this impression.");

public interface IProductImpressionRepository
{
    Task<ImpressionItemContext?> FindItemContextAsync(Guid purchaseItemId, CancellationToken cancellationToken);

    void Add(ProductImpression impression);

    /// <summary>Tracked, including withdrawn ones.</summary>
    Task<ProductImpression?> FindForUpdateAsync(Guid impressionId, CancellationToken cancellationToken);

    /// <summary>Visible (not withdrawn) impressions, newest first.</summary>
    Task<IReadOnlyList<ImpressionView>> ListAsync(ImpressionFilter filter, CancellationToken cancellationToken);

    Task<IReadOnlyList<RateablePurchaseItemView>> ListUnratedAsync(
        string userId,
        DateTimeOffset since,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>The product's purchase lines, newest purchase first.</summary>
    Task<IReadOnlyList<RateablePurchaseItemView>> ListProductLinesAsync(
        Guid productId,
        int limit,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>Exactly one of the scopes is set.</summary>
public sealed record ImpressionFilter(
    Guid? ProductId = null,
    Guid? PurchaseItemId = null,
    Guid? PurchaseId = null,
    Guid? ImpressionId = null,
    int Limit = ImpressionFilter.MaximumLimit)
{
    public const int MaximumLimit = 500;
}

/// <summary>
/// Impression photos live in the same storage as receipts (local folder or S3)
/// under their own prefix, and only as the compressed, metadata-free copy.
/// </summary>
public interface IImpressionPhotoStore
{
    /// <exception cref="InvalidDataException">Empty, too large, not JPEG/PNG/WebP, or undecodable.</exception>
    Task<StoredImpressionPhoto> StoreAsync(Guid impressionId, Stream photo, CancellationToken cancellationToken);

    Task<Stream?> TryOpenReadAsync(string storageKey, CancellationToken cancellationToken);

    Task DeleteAsync(string storageKey, CancellationToken cancellationToken);
}
