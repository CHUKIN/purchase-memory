namespace PurchaseMemory.Application.Receipts;

public sealed class ReceiptCaptureNotFoundException(Guid receiptId)
    : Exception($"Receipt '{receiptId}' was not found.")
{
    public Guid ReceiptId { get; } = receiptId;
}
