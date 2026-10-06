using PurchaseMemory.Domain.Receipts;

namespace PurchaseMemory.Application.Receipts;

public sealed class ReceiptProcessingStateException(
    Guid receiptId,
    ReceiptProcessingStatus currentStatus,
    string requestedOperation)
    : Exception(
        $"Receipt '{receiptId}' is in status '{currentStatus}' and cannot be used for " +
        $"operation '{requestedOperation}'.")
{
    public Guid ReceiptId { get; } = receiptId;

    public ReceiptProcessingStatus CurrentStatus { get; } = currentStatus;

    public string RequestedOperation { get; } = requestedOperation;
}
