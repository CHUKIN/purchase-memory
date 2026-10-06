namespace PurchaseMemory.Application.Receipts;

public sealed record StoredReceiptImage(
    string StorageKey,
    string ContentType,
    long SizeBytes,
    string Sha256);

/// <summary>
/// Why an image is being read. The store decides which stored copy serves the
/// purpose, so callers never need to know whether a display copy exists.
/// </summary>
public enum ReceiptImagePurpose
{
    /// <summary>The bytes exactly as uploaded.</summary>
    Original,

    /// <summary>What people see in the app: the compressed, metadata-free copy.</summary>
    Viewing,

    /// <summary>What the recognition pipeline receives; configurable.</summary>
    Analysis
}

/// <param name="ContentType">
/// Null when the stream is the original upload, whose type is recorded on the
/// image part; otherwise the type of the copy actually returned.
/// </param>
public sealed record ReceiptImageContent(Stream Content, string? ContentType);

public interface IReceiptImageStore
{
    Task<StoredReceiptImage> StoreAsync(
        Guid receiptId,
        int position,
        Stream image,
        string declaredContentType,
        CancellationToken cancellationToken);

    Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken);

    /// <summary>
    /// Stores without derived copies simply serve the original for every purpose.
    /// </summary>
    async Task<ReceiptImageContent> OpenReadAsync(
        string storageKey,
        ReceiptImagePurpose purpose,
        CancellationToken cancellationToken) =>
        new(await OpenReadAsync(storageKey, cancellationToken), ContentType: null);

    Task DeleteAsync(string storageKey, CancellationToken cancellationToken);

    Task DeleteReceiptAsync(Guid receiptId, CancellationToken cancellationToken);
}
