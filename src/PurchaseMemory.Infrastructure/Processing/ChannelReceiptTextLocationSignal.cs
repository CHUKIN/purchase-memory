using System.Threading.Channels;
using PurchaseMemory.Application.Receipts.TextLocation;

namespace PurchaseMemory.Infrastructure.Processing;

/// <summary>
/// A doorbell for the text-location worker: any number of rings while it is
/// busy collapse into one, and ringing never blocks. The worker finds the
/// actual work in the database, so a lost ring only costs an idle poll.
/// </summary>
public sealed class ChannelReceiptTextLocationSignal : IReceiptTextLocationSignal
{
    private readonly Channel<bool> _channel = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

    public void Notify() => _channel.Writer.TryWrite(true);

    /// <summary>Returns when rung or after <paramref name="timeout"/>, whichever comes first.</summary>
    public async Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await _channel.Reader.ReadAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The idle poll is due.
        }
    }
}
