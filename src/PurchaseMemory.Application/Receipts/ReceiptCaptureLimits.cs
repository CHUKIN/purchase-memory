namespace PurchaseMemory.Application.Receipts;

public static class ReceiptCaptureLimits
{
    public const int MaximumPartCount = 8;
    public const long MaximumPartSizeBytes = 15 * 1024 * 1024;
    public const long MaximumTotalSizeBytes = 60 * 1024 * 1024;
    public const long MaximumMultipartRequestSizeBytes = MaximumTotalSizeBytes + (2 * 1024 * 1024);
    public const int MaximumRecentCount = 50;
}
