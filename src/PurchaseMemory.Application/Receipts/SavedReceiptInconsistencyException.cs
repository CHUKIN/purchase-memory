namespace PurchaseMemory.Application.Receipts;

public sealed class SavedReceiptInconsistencyException(Guid receiptId, Guid purchaseId)
    : Exception(
        $"Receipt '{receiptId}' points to purchase '{purchaseId}', but no purchase exists for that source receipt.")
{
    public Guid ReceiptId { get; } = receiptId;

    public Guid PurchaseId { get; } = purchaseId;
}
