namespace PurchaseMemory.Domain.Receipts;

/// <summary>
/// The kind of change a review made to a recognized receipt field or item.
/// Minimal set covering what <c>ReceiptCapture.ReplaceRecognizedDetails</c> can actually do.
/// </summary>
public enum ReceiptReviewAuditChangeType
{
    Modified = 0,
    ItemAdded = 1,
    ItemRemoved = 2,
}
