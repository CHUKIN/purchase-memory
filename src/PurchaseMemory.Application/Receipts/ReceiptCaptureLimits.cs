namespace PurchaseMemory.Application.Receipts;

public static class ReceiptCaptureLimits
{
    public const int MaximumPartCount = 8;
    public const long MaximumPartSizeBytes = 15 * 1024 * 1024;
    public const long MaximumTotalSizeBytes = 60 * 1024 * 1024;
    public const long MaximumMultipartRequestSizeBytes = MaximumTotalSizeBytes + (2 * 1024 * 1024);
    public const int MaximumRecentCount = 50;

    /// <summary>
    /// How far back the capture screen looks for the caller's own unsaved
    /// uploads, so a batch interrupted by a reload or a killed PWA can be
    /// picked up again. A day covers "uploaded in the evening, checked in the
    /// morning" without turning the panel into a second review queue.
    /// </summary>
    public static readonly TimeSpan UnsavedUploadsWindow = TimeSpan.FromHours(24);

    /// <summary>Two full batches; older unsaved receipts stay in «Нужно проверить» and the home list.</summary>
    public const int MaximumUnsavedUploadsCount = 20;
}
