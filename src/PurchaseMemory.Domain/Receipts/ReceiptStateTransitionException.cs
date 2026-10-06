namespace PurchaseMemory.Domain.Receipts;

public sealed class ReceiptStateTransitionException(
    Guid receiptId,
    ReceiptProcessingStatus currentStatus,
    ReceiptProcessingStatus targetStatus)
    : InvalidOperationException(
        $"Receipt '{receiptId}' cannot transition from '{currentStatus}' to '{targetStatus}'.")
{
    public Guid ReceiptId { get; } = receiptId;

    public ReceiptProcessingStatus CurrentStatus { get; } = currentStatus;

    public ReceiptProcessingStatus TargetStatus { get; } = targetStatus;
}
