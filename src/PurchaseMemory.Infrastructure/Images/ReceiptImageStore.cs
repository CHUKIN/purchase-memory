using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PurchaseMemory.Application.Receipts;

namespace PurchaseMemory.Infrastructure.Images;

/// <summary>
/// Validates and stores receipt photos on any <see cref="IReceiptBlobStorage"/>.
/// Each part keeps its original (<c>{receipt}/part-000.jpg</c>) plus a display
/// copy beside it (<c>{receipt}/part-000.display.jpg</c>). Receipts stored before
/// display copies existed simply fall back to the original.
/// </summary>
public sealed partial class ReceiptImageStore : IReceiptImageStore
{
    private const string DisplaySuffix = ".display.jpg";
    private const int BufferSize = 64 * 1024;

    private readonly IReceiptBlobStorage _storage;
    private readonly ReceiptImageStorageOptions _options;
    private readonly ReceiptImageCompressor _compressor;
    private readonly ILogger<ReceiptImageStore> _logger;

    public ReceiptImageStore(
        IReceiptBlobStorage storage,
        IOptions<ReceiptImageStorageOptions> options,
        ILogger<ReceiptImageStore> logger)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _storage = storage;
        _options = options.Value;
        _logger = logger;
        if (_options.MaximumFileSizeBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Maximum receipt image size must be positive.");
        }

        if (_options.AnalysisSource == ReceiptImagePurpose.Analysis)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "ReceiptImages:AnalysisSource must be Original or Viewing.");
        }

        _compressor = new ReceiptImageCompressor(_options.DisplayCopy);
    }

    public async Task<StoredReceiptImage> StoreAsync(
        Guid receiptId,
        int position,
        Stream image,
        string declaredContentType,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentException.ThrowIfNullOrWhiteSpace(declaredContentType);
        if (receiptId == Guid.Empty)
        {
            throw new ArgumentException("Receipt identifier cannot be empty.", nameof(receiptId));
        }

        if (position is < 0 or >= ReceiptCaptureLimits.MaximumPartCount)
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        var folder = ReceiptFolder(receiptId);
        try
        {
            var bytes = await ReadWithinLimitAsync(image, cancellationToken);
            var format = DetectFormat(bytes);
            var key = $"{folder}/part-{position:D3}{format.Extension}";

            await _storage.WriteAsync(key, bytes, format.ContentType, cancellationToken);
            await StoreDisplayCopyAsync(key, bytes, cancellationToken);

            return new StoredReceiptImage(
                key,
                format.ContentType,
                bytes.Length,
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        }
        catch
        {
            await _storage.DeleteFolderAsync(folder, CancellationToken.None);
            throw;
        }
    }

    public async Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken)
    {
        return await _storage.TryOpenReadAsync(storageKey, cancellationToken)
            ?? throw new FileNotFoundException("Receipt image was not found in storage.");
    }

    public async Task<ReceiptImageContent> OpenReadAsync(
        string storageKey,
        ReceiptImagePurpose purpose,
        CancellationToken cancellationToken)
    {
        var source = purpose == ReceiptImagePurpose.Analysis ? _options.AnalysisSource : purpose;
        if (source == ReceiptImagePurpose.Viewing)
        {
            var display = await _storage.TryOpenReadAsync(DisplayKey(storageKey), cancellationToken);
            if (display is not null)
            {
                return new ReceiptImageContent(display, ReceiptImageCompressor.ContentType);
            }
        }

        return new ReceiptImageContent(await OpenReadAsync(storageKey, cancellationToken), ContentType: null);
    }

    public async Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
    {
        await _storage.DeleteAsync(DisplayKey(storageKey), cancellationToken);
        await _storage.DeleteAsync(storageKey, cancellationToken);
    }

    public Task DeleteReceiptAsync(Guid receiptId, CancellationToken cancellationToken)
    {
        if (receiptId == Guid.Empty)
        {
            throw new ArgumentException("Receipt identifier cannot be empty.", nameof(receiptId));
        }

        return _storage.DeleteFolderAsync(ReceiptFolder(receiptId), cancellationToken);
    }

    public static string DisplayKey(string storageKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);
        var normalized = storageKey.Replace('\\', '/');
        var slash = normalized.LastIndexOf('/');
        var dot = normalized.LastIndexOf('.');
        var stem = dot > slash ? normalized[..dot] : normalized;
        return stem + DisplaySuffix;
    }

    private static string ReceiptFolder(Guid receiptId) => receiptId.ToString("N");

    private async Task StoreDisplayCopyAsync(string key, byte[] original, CancellationToken cancellationToken)
    {
        if (!_options.DisplayCopy.Enabled)
        {
            return;
        }

        ReceiptDisplayCopy? copy;
        try
        {
            copy = _compressor.TryCreate(original);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            DisplayCopyFailed(_logger, exception, key);
            return;
        }

        if (copy is null)
        {
            // Magic bytes looked right but the image does not decode; the
            // original is still kept and served, exactly as before copies existed.
            DisplayCopySkipped(_logger, key);
            return;
        }

        await _storage.WriteAsync(DisplayKey(key), copy.Content, ReceiptImageCompressor.ContentType, cancellationToken);
        DisplayCopyStored(_logger, key, original.Length, copy.Content.Length, copy.Width, copy.Height);
    }

    private async Task<byte[]> ReadWithinLimitAsync(Stream image, CancellationToken cancellationToken)
    {
        var limit = _options.MaximumFileSizeBytes;
        using var buffer = new MemoryStream();
        var chunk = new byte[BufferSize];
        while (true)
        {
            var read = await image.ReadAsync(chunk, cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (buffer.Length + read > limit)
            {
                throw new InvalidDataException($"Receipt image exceeds the {limit / 1024 / 1024} MB limit.");
            }

            buffer.Write(chunk, 0, read);
        }

        if (buffer.Length == 0)
        {
            throw new InvalidDataException("Receipt image is empty.");
        }

        return buffer.ToArray();
    }

    // A byte array, not a "\x89PNG…"u8 literal: in C# that escape is the
    // character U+0089, which UTF-8 encodes as two bytes (C2 89).
    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    internal static ImageFormat DetectFormat(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 8 && header[..8].SequenceEqual(PngSignature))
        {
            return new ImageFormat(".png", "image/png");
        }

        if (header.Length >= 3 && header[0] == 0xff && header[1] == 0xd8 && header[2] == 0xff)
        {
            return new ImageFormat(".jpg", "image/jpeg");
        }

        if (header.Length >= 12
            && header[..4].SequenceEqual("RIFF"u8)
            && header.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return new ImageFormat(".webp", "image/webp");
        }

        throw new InvalidDataException("Only JPEG, PNG and WebP receipt images are supported.");
    }

    internal sealed record ImageFormat(string Extension, string ContentType);

    [LoggerMessage(EventId = 1201, Level = LogLevel.Information,
        Message = "Receipt display copy stored for {StorageKey}: {OriginalBytes} -> {DisplayBytes} bytes, {Width}x{Height}")]
    private static partial void DisplayCopyStored(
        ILogger logger, string storageKey, long originalBytes, long displayBytes, int width, int height);

    [LoggerMessage(EventId = 1202, Level = LogLevel.Warning,
        Message = "Receipt image {StorageKey} could not be decoded; no display copy, the original will be served")]
    private static partial void DisplayCopySkipped(ILogger logger, string storageKey);

    [LoggerMessage(EventId = 1203, Level = LogLevel.Warning,
        Message = "Receipt display copy for {StorageKey} failed; the original will be served")]
    private static partial void DisplayCopyFailed(ILogger logger, Exception exception, string storageKey);
}
