namespace PurchaseMemory.Application.Receipts;

/// <summary>
/// A recognition failure whose message is safe to show in the receipt review UI.
/// </summary>
public sealed class ReceiptExtractionException : Exception
{
    public ReceiptExtractionException(string message)
        : base(message)
    {
    }

    public ReceiptExtractionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
