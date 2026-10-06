namespace PurchaseMemory.Domain.Receipts;

public enum ReceiptProcessingStatus
{
    Uploaded,
    Queued,
    Processing,
    ReadyForReview,
    Failed,
    Saved
}
