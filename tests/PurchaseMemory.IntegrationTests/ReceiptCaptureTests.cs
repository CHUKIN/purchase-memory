using PurchaseMemory.Domain.Receipts;

namespace PurchaseMemory.IntegrationTests;

public sealed class ReceiptCaptureTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ReceiptMovesThroughReviewAndSaveLifecycle()
    {
        var receipt = CreateUploadedReceipt();

        receipt.Queue(CreatedAt.AddMinutes(1));
        receipt.StartProcessing(CreatedAt.AddMinutes(2));
        receipt.CompleteProcessing(
            "  Lidl  ",
            new DateTimeOffset(2026, 7, 15, 13, 15, 0, TimeSpan.FromHours(2)),
            "pln",
            7.99m,
            [ReceiptCaptureItem.Create(0, "MLEKO", 1, "szt", 7.99m, 7.99m)],
            "ollama",
            "vision-test",
            CreatedAt.AddMinutes(3));

        receipt.ApplyReview(
            "Lidl Polska",
            receipt.PurchasedAt,
            receipt.Currency,
            receipt.ReceiptTotal,
            [ReceiptCaptureItem.Create(0, "MLEKO 3,2%", 1, "szt", 7.99m, 7.99m)],
            CreatedAt.AddMinutes(4));

        var purchaseId = Guid.NewGuid();
        receipt.MarkSaved(purchaseId, CreatedAt.AddMinutes(5));
        receipt.MarkSaved(purchaseId, CreatedAt.AddMinutes(6));

        Assert.Equal(ReceiptProcessingStatus.Saved, receipt.Status);
        Assert.Equal("Lidl Polska", receipt.MerchantName);
        Assert.Equal("PLN", receipt.Currency);
        Assert.Equal("MLEKO 3,2%", Assert.Single(receipt.Items).Name);
        Assert.Equal(purchaseId, receipt.SavedPurchaseId);
        Assert.Equal(CreatedAt.AddMinutes(5), receipt.UpdatedAt);
    }

    [Fact]
    public void ReceiptRejectsInvalidStateTransition()
    {
        var receipt = CreateUploadedReceipt();

        var exception = Assert.Throws<ReceiptStateTransitionException>(() =>
            receipt.StartProcessing(CreatedAt.AddMinutes(1)));

        Assert.Equal(ReceiptProcessingStatus.Uploaded, exception.CurrentStatus);
        Assert.Equal(ReceiptProcessingStatus.Processing, exception.TargetStatus);
        Assert.Equal(ReceiptProcessingStatus.Uploaded, receipt.Status);
    }

    [Fact]
    public void FailedReceiptCanBeQueuedForRetry()
    {
        var receipt = CreateUploadedReceipt();
        receipt.Queue(CreatedAt.AddMinutes(1));
        receipt.StartProcessing(CreatedAt.AddMinutes(2));
        receipt.FailProcessing("Model is temporarily unavailable.", CreatedAt.AddMinutes(3));

        receipt.Queue(CreatedAt.AddMinutes(4));

        Assert.Equal(ReceiptProcessingStatus.Queued, receipt.Status);
        Assert.Null(receipt.ErrorMessage);
    }

    private static ReceiptCapture CreateUploadedReceipt()
    {
        return ReceiptCapture.CreateUploaded(
            Guid.NewGuid(),
            [ReceiptImagePart.Create(0, "receipt/part-000.jpg", "image/jpeg", 128, new string('a', 64))],
            CreatedAt);
    }
}
