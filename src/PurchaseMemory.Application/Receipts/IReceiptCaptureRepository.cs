using PurchaseMemory.Domain.Receipts;

namespace PurchaseMemory.Application.Receipts;

public interface IReceiptCaptureRepository
{
    Task<ReceiptCapture?> AddOrFindExactDuplicateAsync(
        ReceiptCapture receipt,
        CancellationToken cancellationToken);

    Task<ReceiptCapture?> FindByIdAsync(Guid receiptId, CancellationToken cancellationToken);

    Task<ReceiptCapture?> FindByImageSetSha256Async(
        string imageSetSha256,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ReceiptCapture>> ListRecentAsync(
        int limit,
        CancellationToken cancellationToken);

    /// <summary>Receipts this user uploaded since <paramref name="since"/> that are not saved yet, newest first.</summary>
    Task<IReadOnlyList<ReceiptCapture>> ListUnsavedUploadedByAsync(
        string uploaderUserId,
        DateTimeOffset since,
        int limit,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ReceiptCapture>> ListPendingAsync(CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
