namespace PurchaseMemory.Domain.Products;

/// <summary>
/// A photo of a product's package, shared by the household. Only a compressed,
/// metadata-free copy and a small thumbnail are stored (the keys point into
/// the photo storage). Exactly one photo of a product with photos is the main
/// one; the media service keeps that rule.
/// </summary>
public sealed class ProductPhoto
{
    /// <summary>A gallery of the same pack, not an album: more than this is refused.</summary>
    public const int MaximumPerProduct = 20;

    public const int MaximumClientIdLength = 64;

    private ProductPhoto()
    {
        // Required by Entity Framework Core.
    }

    public Guid Id { get; private set; }

    public Guid ProductId { get; private set; }

    public string StorageKey { get; private set; } = string.Empty;

    public string ThumbnailKey { get; private set; } = string.Empty;

    public int Width { get; private set; }

    public int Height { get; private set; }

    /// <summary>Bytes of the stored copy.</summary>
    public long SizeBytes { get; private set; }

    public long ThumbnailSizeBytes { get; private set; }

    public bool IsMain { get; private set; }

    /// <summary>
    /// The id the uploading device gave the photo (stage 1b kept photos on the
    /// phone). Uploading the same one again for the same product returns this
    /// row instead of a second copy, so the one-time move from a phone can be
    /// repeated safely.
    /// </summary>
    public string? ClientId { get; private set; }

    public string CreatedBy { get; private set; } = string.Empty;

    public string? CreatedByEmail { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static ProductPhoto Create(
        Guid id,
        Guid productId,
        StoredProductPhotoFiles files,
        bool isMain,
        string? clientId,
        string createdBy,
        string? createdByEmail,
        DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentException.ThrowIfNullOrWhiteSpace(files.StorageKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(files.ThumbnailKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(createdBy);
        if (id == Guid.Empty || productId == Guid.Empty)
        {
            throw new ArgumentException("Photo and product identifiers cannot be empty.");
        }

        if (clientId is not null && (clientId.Length is 0 or > MaximumClientIdLength))
        {
            throw new ArgumentException($"Client id must be 1–{MaximumClientIdLength} characters.", nameof(clientId));
        }

        return new ProductPhoto
        {
            Id = id,
            ProductId = productId,
            StorageKey = files.StorageKey,
            ThumbnailKey = files.ThumbnailKey,
            Width = files.Width,
            Height = files.Height,
            SizeBytes = files.SizeBytes,
            ThumbnailSizeBytes = files.ThumbnailSizeBytes,
            IsMain = isMain,
            ClientId = clientId,
            CreatedBy = createdBy.Length <= 200 ? createdBy : createdBy[..200],
            CreatedByEmail = createdByEmail is null || createdByEmail.Length <= 320 ? createdByEmail : createdByEmail[..320],
            CreatedAt = createdAt
        };
    }

    public void SetMain(bool isMain) => IsMain = isMain;
}

/// <summary>Where the photo storage put a product photo and its thumbnail.</summary>
public sealed record StoredProductPhotoFiles(
    string StorageKey,
    string ThumbnailKey,
    int Width,
    int Height,
    long SizeBytes,
    long ThumbnailSizeBytes);
