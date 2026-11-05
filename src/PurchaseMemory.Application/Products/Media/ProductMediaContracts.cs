using PurchaseMemory.Application.Products.PhotoNames;
using PurchaseMemory.Domain.Products;

namespace PurchaseMemory.Application.Products.Media;

/// <summary>A product's photos (oldest first), barcodes and «Больше не покупать» mark.</summary>
public sealed record ProductMediaView(
    Guid ProductId,
    IReadOnlyList<ProductPhotoView> Photos,
    Guid? MainPhotoId,
    IReadOnlyList<ProductBarcodeView> Barcodes,
    DateTimeOffset? AvoidSince);

public sealed record ProductPhotoView(
    Guid Id,
    DateTimeOffset CreatedAt,
    int Width,
    int Height,
    long SizeBytes,
    bool IsMain);

public sealed record ProductBarcodeView(string Code, ProductBarcodeSource Source, DateTimeOffset CreatedAt);

/// <param name="Created">False when the same client photo id was already uploaded for this product.</param>
/// <param name="FirstPhoto">The product had no photos before this upload (the first-photo naming rule).</param>
public sealed record ProductPhotoUploadResult(ProductPhotoView Photo, bool Created, bool FirstPhoto);

/// <param name="Created">False when the product already carried the code.</param>
public sealed record ProductBarcodeAddResult(ProductBarcodeView Barcode, bool Created);

/// <summary>What a scanned code is: the products carrying it (normally one) and a «remember» entry.</summary>
public sealed record BarcodeLookup(string Code, IReadOnlyList<Guid> ProductIds, RememberedBarcodeView? Remembered);

public sealed record RememberedBarcodeView(string Code, DateTimeOffset RememberedAt, OpenFoodFactsProduct? Off);

/// <summary>A stored photo's bytes; the caller disposes the stream.</summary>
public sealed record ProductPhotoContent(Stream Content, string ContentType);

/// <summary>A barcode that is not a valid EAN-8/13, UPC-A or UPC-E.</summary>
public sealed class InvalidBarcodeException()
    : ArgumentException("The barcode must be 8, 12 or 13 digits with a correct check digit.");

/// <summary>One code, one product: the code already belongs to another product of the household.</summary>
public sealed class BarcodeTakenException(string code, Guid productId)
    : Exception($"Barcode {code} already belongs to another product.")
{
    public string Code { get; } = code;

    public Guid ProductId { get; } = productId;
}

public sealed class ProductPhotoNotFoundException(Guid photoId)
    : Exception($"Photo {photoId} was not found on this product.");

public sealed class ProductBarcodeNotFoundException(string code)
    : Exception($"Barcode {code} is not on this product.");

/// <summary>Too many photos or barcodes on one product.</summary>
public sealed class ProductMediaLimitException(string code, string message) : Exception(message)
{
    /// <summary>Machine-readable: <c>photoLimit</c> or <c>barcodeLimit</c>.</summary>
    public string Code { get; } = code;
}

/// <summary>The database refused a write (unique index, a row deleted in between).</summary>
public sealed class ProductMediaSaveException(Exception innerException)
    : Exception("The product's photos or barcodes were changed at the same time.", innerException);

public interface IProductMediaService
{
    /// <exception cref="ProductNotFoundException">No such product.</exception>
    Task<ProductMediaView> GetAsync(Guid productId, CancellationToken cancellationToken);

    /// <summary>
    /// Checks, compresses and stores one photo. The first photo of a product
    /// becomes the main one. With <paramref name="clientId"/> repeated for the
    /// same product, the stored photo is returned and nothing is added.
    /// </summary>
    /// <exception cref="InvalidProductPhotoException">Empty, too large or not a decodable JPEG/PNG/WebP.</exception>
    /// <exception cref="ProductMediaLimitException">The product has the most photos it may have.</exception>
    Task<ProductPhotoUploadResult> AddPhotoAsync(
        Guid productId,
        ReadOnlyMemory<byte> photo,
        string? clientId,
        Receipts.ReceiptActor actor,
        CancellationToken cancellationToken);

    /// <summary>Deletes a photo; if it was the main one, the oldest remaining becomes main.</summary>
    Task RemovePhotoAsync(Guid productId, Guid photoId, Receipts.ReceiptActor actor, CancellationToken cancellationToken);

    Task<ProductMediaView> SetMainPhotoAsync(Guid productId, Guid photoId, Receipts.ReceiptActor actor, CancellationToken cancellationToken);

    /// <summary>The compressed copy or its thumbnail; null when there is no such photo.</summary>
    Task<ProductPhotoContent?> OpenPhotoAsync(Guid photoId, bool thumbnail, CancellationToken cancellationToken);

    /// <summary>Adding a code the product already has is a success that changes nothing. Forgets a remembered code.</summary>
    /// <exception cref="InvalidBarcodeException">Not a shop barcode.</exception>
    /// <exception cref="BarcodeTakenException">The code is on another product.</exception>
    Task<ProductBarcodeAddResult> AddBarcodeAsync(
        Guid productId,
        string code,
        ProductBarcodeSource source,
        Receipts.ReceiptActor actor,
        CancellationToken cancellationToken);

    Task RemoveBarcodeAsync(Guid productId, string code, Receipts.ReceiptActor actor, CancellationToken cancellationToken);

    /// <exception cref="InvalidBarcodeException">Not a shop barcode.</exception>
    Task<BarcodeLookup> FindByBarcodeAsync(string code, CancellationToken cancellationToken);

    /// <param name="since">When the mark was first made (the one-time move from a phone); now when null.</param>
    Task<ProductMediaView> SetAvoidAsync(
        Guid productId,
        bool avoid,
        DateTimeOffset? since,
        Receipts.ReceiptActor actor,
        CancellationToken cancellationToken);

    /// <summary>Newest first.</summary>
    Task<IReadOnlyList<RememberedBarcodeView>> ListRememberedAsync(CancellationToken cancellationToken);

    /// <summary>
    /// «Запомнить на потом». A code that already belongs to a product is not
    /// remembered (the existing link is what the person needs) and answers
    /// <see cref="BarcodeTakenException"/>.
    /// </summary>
    Task<RememberedBarcodeView> RememberAsync(
        string code,
        OpenFoodFactsProduct? off,
        Receipts.ReceiptActor actor,
        CancellationToken cancellationToken);

    /// <summary>Drops a remembered code; forgetting an unknown code is a no-op.</summary>
    Task ForgetAsync(string code, CancellationToken cancellationToken);

    /// <summary>Main photo per product, for list thumbnails; products without photos are absent.</summary>
    Task<IReadOnlyDictionary<Guid, Guid>> MainPhotoIdsAsync(IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken);
}

/// <summary>Persistence for <see cref="IProductMediaService"/>.</summary>
public interface IProductMediaRepository
{
    /// <summary>Tracked.</summary>
    Task<Product?> FindProductForUpdateAsync(Guid productId, CancellationToken cancellationToken);

    Task<bool> ProductExistsAsync(Guid productId, CancellationToken cancellationToken);

    /// <summary>Tracked, oldest first.</summary>
    Task<IReadOnlyList<ProductPhoto>> ListPhotosForUpdateAsync(Guid productId, CancellationToken cancellationToken);

    /// <summary>Not tracked.</summary>
    Task<ProductPhoto?> FindPhotoAsync(Guid photoId, CancellationToken cancellationToken);

    /// <summary>Not tracked.</summary>
    Task<ProductPhoto?> FindPhotoByClientIdAsync(Guid productId, string clientId, CancellationToken cancellationToken);

    /// <summary>Tracked, oldest first.</summary>
    Task<IReadOnlyList<ProductBarcode>> ListBarcodesForUpdateAsync(Guid productId, CancellationToken cancellationToken);

    /// <summary>Not tracked; the code is normalized.</summary>
    Task<ProductBarcode?> FindBarcodeAsync(string code, CancellationToken cancellationToken);

    void AddPhoto(ProductPhoto photo);

    void RemovePhoto(ProductPhoto photo);

    void AddBarcode(ProductBarcode barcode);

    void RemoveBarcode(ProductBarcode barcode);

    /// <summary>Tracked.</summary>
    Task<RememberedBarcode?> FindRememberedForUpdateAsync(string code, CancellationToken cancellationToken);

    /// <summary>Newest first, at most <paramref name="limit"/>.</summary>
    Task<IReadOnlyList<RememberedBarcode>> ListRememberedAsync(int limit, CancellationToken cancellationToken);

    void AddRemembered(RememberedBarcode remembered);

    void RemoveRemembered(RememberedBarcode remembered);

    /// <summary>Deletes all but the newest <paramref name="keep"/> remembered codes; runs immediately.</summary>
    Task TrimRememberedAsync(int keep, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, Guid>> MainPhotoIdsAsync(IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken);

    void AddAuditEntry(ProductAuditEntry entry);

    /// <exception cref="ProductMediaSaveException">A unique index or a vanished row refused the write.</exception>
    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>Forgets tracked changes after a refused save, so the scope can still read.</summary>
    void DiscardChanges();
}

/// <summary>
/// Product photos live in the same storage as receipts (local folder or the
/// Supabase S3 bucket) under their own <c>products/</c> prefix, as a
/// compressed, metadata-free copy plus a small thumbnail.
/// </summary>
public interface IProductPhotoStore
{
    /// <exception cref="InvalidProductPhotoException">The bytes cannot be decoded as an image.</exception>
    Task<StoredProductPhotoFiles> StoreAsync(Guid photoId, ProductPhotoImage image, CancellationToken cancellationToken);

    Task<Stream?> TryOpenReadAsync(string storageKey, CancellationToken cancellationToken);

    /// <summary>Deletes the copy and the thumbnail; missing files are fine.</summary>
    Task DeleteAsync(Guid photoId, CancellationToken cancellationToken);
}
