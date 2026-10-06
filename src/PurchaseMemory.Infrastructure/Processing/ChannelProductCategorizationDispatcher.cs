using System.Threading.Channels;
using PurchaseMemory.Application.Products.Categories;

namespace PurchaseMemory.Infrastructure.Processing;

/// <summary>
/// In-memory hand-off from the save path to the categorization worker. When
/// the buffer is full the newest request is dropped rather than blocking a
/// save; those products stay unclassified and the backfill picks them up.
/// </summary>
public sealed class ChannelProductCategorizationDispatcher : IProductCategorizationDispatcher
{
    private readonly Channel<IReadOnlyCollection<Guid>> _channel = Channel.CreateBounded<IReadOnlyCollection<Guid>>(
        new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

    public void Enqueue(IReadOnlyCollection<Guid> productIds)
    {
        ArgumentNullException.ThrowIfNull(productIds);
        if (productIds.Count > 0)
        {
            _channel.Writer.TryWrite(productIds.ToArray());
        }
    }

    public ValueTask<IReadOnlyCollection<Guid>> DequeueAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAsync(cancellationToken);
}
