using System.Threading.Channels;
using PurchaseMemory.Application.Receipts;

namespace PurchaseMemory.Infrastructure.Processing;

public sealed class ChannelReceiptProcessingDispatcher : IReceiptProcessingDispatcher
{
    private readonly Channel<Guid> _channel = Channel.CreateBounded<Guid>(
        new BoundedChannelOptions(32)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

    public ValueTask EnqueueAsync(Guid receiptId, CancellationToken cancellationToken)
    {
        if (receiptId == Guid.Empty)
        {
            throw new ArgumentException("Receipt identifier cannot be empty.", nameof(receiptId));
        }

        return _channel.Writer.WriteAsync(receiptId, cancellationToken);
    }

    public ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken)
    {
        return _channel.Reader.ReadAsync(cancellationToken);
    }
}
