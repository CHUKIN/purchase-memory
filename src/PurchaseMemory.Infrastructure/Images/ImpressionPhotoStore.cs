using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PurchaseMemory.Application.Impressions;

namespace PurchaseMemory.Infrastructure.Images;

/// <summary>
/// Stores impression photos on the same <see cref="IReceiptBlobStorage"/> as
/// receipts (local folder or S3), under <c>impressions/{id}/photo.jpg</c>.
/// Unlike receipts, only the display copy is kept: an impression photo is not
/// evidence that ever needs the original bytes, and not keeping them means its
/// EXIF (GPS position, camera, capture time) never reaches storage at all.
/// </summary>
public sealed partial class ImpressionPhotoStore : IImpressionPhotoStore
{
    public const string KeyPrefix = "impressions";
    private const int BufferSize = 64 * 1024;

    private readonly IReceiptBlobStorage _storage;
    private readonly ReceiptImageCompressor _compressor;
    private readonly long _maximumBytes;
    private readonly ILogger<ImpressionPhotoStore> _logger;

    public ImpressionPhotoStore(
        IReceiptBlobStorage storage,
        IOptions<ReceiptImageStorageOptions> options,
        ILogger<ImpressionPhotoStore> logger)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _storage = storage;
        _logger = logger;
        _maximumBytes = options.Value.MaximumFileSizeBytes;
        // Same size and quality limits as receipt display copies, applied even
        // when receipt display copies are switched off.
        _compressor = new ReceiptImageCompressor(options.Value.DisplayCopy);
    }

    public async Task<StoredImpressionPhoto> StoreAsync(Guid impressionId, Stream photo, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(photo);
        if (impressionId == Guid.Empty)
        {
            throw new ArgumentException("Impression identifier cannot be empty.", nameof(impressionId));
        }

        var bytes = await ReadWithinLimitAsync(photo, cancellationToken);
        try
        {
            ReceiptImageStore.DetectFormat(bytes);
        }
        catch (InvalidDataException)
        {
            throw new InvalidDataException("Only JPEG, PNG and WebP photos are supported.");
        }

        var copy = _compressor.TryCreate(bytes)
            ?? throw new InvalidDataException("The photo could not be read as an image.");
        var key = $"{KeyPrefix}/{impressionId:N}/photo.jpg";
        await _storage.WriteAsync(key, copy.Content, ReceiptImageCompressor.ContentType, cancellationToken);
        PhotoStored(_logger, impressionId, bytes.Length, copy.Content.Length, copy.Width, copy.Height);
        return new StoredImpressionPhoto(key, ReceiptImageCompressor.ContentType);
    }

    public Task<Stream?> TryOpenReadAsync(string storageKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);
        return _storage.TryOpenReadAsync(storageKey, cancellationToken);
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);
        return _storage.DeleteAsync(storageKey, cancellationToken);
    }

    private async Task<byte[]> ReadWithinLimitAsync(Stream photo, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[BufferSize];
        int read;
        while ((read = await photo.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > _maximumBytes)
            {
                throw new InvalidDataException($"The photo exceeds the {_maximumBytes / 1024 / 1024} MB limit.");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.Length == 0 ? throw new InvalidDataException("The photo is empty.") : buffer.ToArray();
    }

    [LoggerMessage(EventId = 1211, Level = LogLevel.Information,
        Message = "Impression photo stored for {ImpressionId}: {OriginalBytes} -> {StoredBytes} bytes, {Width}x{Height}")]
    private static partial void PhotoStored(
        ILogger logger, Guid impressionId, long originalBytes, long storedBytes, int width, int height);
}
