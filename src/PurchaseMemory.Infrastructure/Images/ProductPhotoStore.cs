using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PurchaseMemory.Application.Products.Media;
using PurchaseMemory.Application.Products.PhotoNames;
using PurchaseMemory.Domain.Products;

namespace PurchaseMemory.Infrastructure.Images;

/// <summary>
/// Stores product photos on the same <see cref="IReceiptBlobStorage"/> as
/// receipts (local folder or the Supabase S3 bucket), under
/// <c>products/{photoId}/photo.jpg</c> and <c>products/{photoId}/thumb.jpg</c>.
/// The key does not contain the product, so a merge only moves the row. Like
/// impression photos, only re-encoded copies are kept: the phone's EXIF (GPS
/// position, camera, capture time) never reaches storage.
/// </summary>
public sealed partial class ProductPhotoStore : IProductPhotoStore
{
    public const string KeyPrefix = "products";

    /// <summary>The phone already sends ~1280 px; the server never keeps more.</summary>
    public const int PhotoLongSidePixels = 1280;

    /// <summary>Lists and the scan result show ~48–96 px squares; 256 px covers 3× screens at ~15–20 KB.</summary>
    public const int ThumbnailLongSidePixels = 256;

    private readonly IReceiptBlobStorage _storage;
    private readonly ReceiptImageCompressor _photo;
    private readonly ReceiptImageCompressor _thumbnail;
    private readonly ILogger<ProductPhotoStore> _logger;

    public ProductPhotoStore(
        IReceiptBlobStorage storage,
        IOptions<ReceiptImageStorageOptions> options,
        ILogger<ProductPhotoStore> logger)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _storage = storage;
        _logger = logger;
        var guard = options.Value.DisplayCopy.MaximumSourcePixels;
        _photo = new ReceiptImageCompressor(new ReceiptDisplayCopyOptions
        {
            MaximumLongSidePixels = PhotoLongSidePixels,
            JpegQuality = 82,
            MaximumSourcePixels = guard
        });
        _thumbnail = new ReceiptImageCompressor(new ReceiptDisplayCopyOptions
        {
            MaximumLongSidePixels = ThumbnailLongSidePixels,
            JpegQuality = 72,
            MaximumSourcePixels = guard
        });
    }

    public static string PhotoKey(Guid photoId) => $"{Folder(photoId)}/photo.jpg";

    public static string ThumbnailKey(Guid photoId) => $"{Folder(photoId)}/thumb.jpg";

    public async Task<StoredProductPhotoFiles> StoreAsync(Guid photoId, ProductPhotoImage image, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (photoId == Guid.Empty)
        {
            throw new ArgumentException("Photo identifier cannot be empty.", nameof(photoId));
        }

        var original = image.Content.ToArray();
        var copy = _photo.TryCreate(original)
            ?? throw new InvalidProductPhotoException(InvalidProductPhotoReason.NotAnImage, "The photo could not be read as an image.");
        // From the copy, not the original: it is already upright and small.
        var thumbnail = _thumbnail.TryCreate(copy.Content)
            ?? throw new InvalidProductPhotoException(InvalidProductPhotoReason.NotAnImage, "The photo could not be read as an image.");

        var photoKey = PhotoKey(photoId);
        var thumbnailKey = ThumbnailKey(photoId);
        await _storage.WriteAsync(photoKey, copy.Content, ReceiptImageCompressor.ContentType, cancellationToken);
        try
        {
            await _storage.WriteAsync(thumbnailKey, thumbnail.Content, ReceiptImageCompressor.ContentType, cancellationToken);
        }
        catch
        {
            await _storage.DeleteFolderAsync(Folder(photoId), CancellationToken.None);
            throw;
        }

        PhotoStored(_logger, photoId, original.Length, copy.Content.Length, thumbnail.Content.Length, copy.Width, copy.Height);
        return new StoredProductPhotoFiles(photoKey, thumbnailKey, copy.Width, copy.Height, copy.Content.Length, thumbnail.Content.Length);
    }

    public Task<Stream?> TryOpenReadAsync(string storageKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);
        return _storage.TryOpenReadAsync(storageKey, cancellationToken);
    }

    public Task DeleteAsync(Guid photoId, CancellationToken cancellationToken) =>
        _storage.DeleteFolderAsync(Folder(photoId), cancellationToken);

    private static string Folder(Guid photoId) => $"{KeyPrefix}/{photoId:N}";

    [LoggerMessage(EventId = 1221, Level = LogLevel.Information,
        Message = "Product photo {PhotoId} stored: {OriginalBytes} -> {StoredBytes} bytes (thumbnail {ThumbnailBytes}), {Width}x{Height}")]
    private static partial void PhotoStored(
        ILogger logger, Guid photoId, long originalBytes, long storedBytes, long thumbnailBytes, int width, int height);
}
