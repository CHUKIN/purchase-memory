namespace PurchaseMemory.Application.Receipts;

public interface IReceiptProcessingDispatcher
{
    ValueTask EnqueueAsync(Guid receiptId, CancellationToken cancellationToken);

    ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken);
}
