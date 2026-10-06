using PurchaseMemory.Application.Purchases;

namespace PurchaseMemory.Application.Receipts;

public interface IReceiptProcessingService
{
    Task<UploadReceiptResult> UploadAsync(
        UploadReceiptCommand command,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ReceiptCaptureView>> ListRecentAsync(
        int limit,
        CancellationToken cancellationToken);

    Task<int> RecoverPendingAsync(CancellationToken cancellationToken);

    Task<ReceiptCaptureView> QueueAsync(Guid receiptId, CancellationToken cancellationToken);

    Task<ReceiptCaptureView> ProcessAsync(Guid receiptId, CancellationToken cancellationToken);

    Task<ReceiptCaptureView> GetAsync(Guid receiptId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ReceiptReviewAuditEntryView>> GetAuditTrailAsync(
        Guid receiptId,
        CancellationToken cancellationToken);

    Task<ReceiptCaptureView> ReviewAsync(
        ReviewReceiptCommand command,
        CancellationToken cancellationToken);

    Task<PurchaseDraftView> SaveAsync(
        SaveReceiptCommand command,
        CancellationToken cancellationToken);
}
