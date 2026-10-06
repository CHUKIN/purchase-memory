namespace PurchaseMemory.Infrastructure.Images;

/// <summary>
/// Dumb byte storage under slash-separated keys. Validation, hashing and
/// derived copies live in <see cref="ReceiptImageStore"/>, so every backend
/// behaves identically and stays trivially small.
/// </summary>
public interface IReceiptBlobStorage
{
    Task WriteAsync(string key, ReadOnlyMemory<byte> content, string contentType, CancellationToken cancellationToken);

    /// <summary>Returns null when the key does not exist.</summary>
    Task<Stream?> TryOpenReadAsync(string key, CancellationToken cancellationToken);

    Task DeleteAsync(string key, CancellationToken cancellationToken);

    /// <summary>Deletes every key that starts with <paramref name="prefix"/> + "/".</summary>
    Task DeleteFolderAsync(string prefix, CancellationToken cancellationToken);
}
