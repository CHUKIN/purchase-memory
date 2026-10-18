using PurchaseMemory.Domain.Receipts;

namespace PurchaseMemory.Application.Receipts.TextLocation;

/// <summary>What an OCR service read on one photo: page size in pixels and the printed lines.</summary>
public sealed record ReceiptTextPage(string Source, double Width, double Height, IReadOnlyList<ReceiptTextLine> Lines);

public enum ReceiptTextLocatorFailure
{
    /// <summary>Network trouble, a timeout, a 5xx or an unfinished analysis: try again later.</summary>
    Transient,

    /// <summary>The service refused this photo (bad format, too large, unreadable): never retry it.</summary>
    Rejected,

    /// <summary>Still rate limited after the allowed waits: pause and retry later.</summary>
    RateLimited,

    /// <summary>The key or endpoint is wrong (401/403/404): pause everything until the configuration changes.</summary>
    NotAuthorized
}

/// <summary>Either a page or a failure; <see cref="RetryAfter"/> is the service's hint, when it gave one.</summary>
public sealed record ReceiptTextLocatorResult(
    ReceiptTextPage? Page,
    ReceiptTextLocatorFailure? Failure,
    TimeSpan? RetryAfter = null)
{
    public static ReceiptTextLocatorResult Read(ReceiptTextPage page) => new(page, null);

    public static ReceiptTextLocatorResult Failed(ReceiptTextLocatorFailure failure, TimeSpan? retryAfter = null) =>
        new(null, failure, retryAfter);
}

/// <summary>
/// Reads printed text with word outlines from a receipt photo (Azure Document
/// Intelligence prebuilt-read in production). Optional: without configuration
/// <see cref="IsConfigured"/> is false, nothing is ever sent and every line
/// keeps the recognition box. Implementations never throw for service
/// failures; they return a <see cref="ReceiptTextLocatorFailure"/>.
/// </summary>
public interface IReceiptTextLocator
{
    bool IsConfigured { get; }

    /// <summary>Largest photo the service accepts; bigger photos are not sent.</summary>
    long MaximumImageBytes { get; }

    Task<ReceiptTextLocatorResult> ReadAsync(
        ReadOnlyMemory<byte> image,
        string contentType,
        CancellationToken cancellationToken);
}

/// <summary>Used where no service is configured (tools, tests): never sends anything.</summary>
public sealed class DisabledReceiptTextLocator : IReceiptTextLocator
{
    public bool IsConfigured => false;

    public long MaximumImageBytes => 0;

    public Task<ReceiptTextLocatorResult> ReadAsync(
        ReadOnlyMemory<byte> image,
        string contentType,
        CancellationToken cancellationToken) =>
        Task.FromResult(ReceiptTextLocatorResult.Failed(ReceiptTextLocatorFailure.NotAuthorized));
}

/// <summary>A receipt the background step may send, without loading its lines.</summary>
public sealed record ReceiptTextLocationCandidate(Guid ReceiptId, DateTimeOffset CreatedAt, IReadOnlyList<ReceiptImagePart> ImageParts);

public interface IReceiptTextLayoutRepository
{
    Task<ReceiptTextLayout?> FindAsync(Guid receiptId, CancellationToken cancellationToken);

    /// <summary>Adds the layout, replacing an older one for the same receipt.</summary>
    Task ReplaceAsync(ReceiptTextLayout layout, CancellationToken cancellationToken);

    /// <summary>
    /// The newest receipt (recognised or saved) that was never tried, or whose
    /// last passing failure is older than <paramref name="retryBefore"/>;
    /// receipts created before <paramref name="createdSince"/> are skipped when it is set.
    /// A failed receipt that already used <paramref name="maximumAttempts"/> tries is
    /// never offered again (e.g. after a crash during its last call).
    /// </summary>
    Task<ReceiptTextLocationCandidate?> FindNextCandidateAsync(
        DateTimeOffset retryBefore,
        DateTimeOffset? createdSince,
        int maximumAttempts,
        CancellationToken cancellationToken);

    /// <summary>
    /// Service calls recorded on receipts last tried at or after <paramref name="since"/>
    /// (a receipt tried in an earlier month too is counted whole: over, never under).
    /// </summary>
    Task<int> CountCallsSinceAsync(DateTimeOffset since, CancellationToken cancellationToken);

    /// <summary>
    /// Forgets unsaved changes of this unit of work (shared with the receipt
    /// repository), so a failed save can be followed by a clean reload.
    /// </summary>
    void DiscardPendingChanges();
}

/// <summary>
/// Wakes the background text-location step when a receipt is ready, so a new
/// receipt does not wait for the next idle poll. Never blocks the caller.
/// </summary>
public interface IReceiptTextLocationSignal
{
    void Notify();
}

public sealed class NullReceiptTextLocationSignal : IReceiptTextLocationSignal
{
    public void Notify()
    {
    }
}
