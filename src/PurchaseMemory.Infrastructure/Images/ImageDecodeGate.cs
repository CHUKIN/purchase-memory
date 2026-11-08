using System.Collections.Concurrent;

namespace PurchaseMemory.Infrastructure.Images;

/// <summary>
/// Process-wide budget for pixels being decoded at the same moment. A decode
/// holds about 4 bytes per pixel, so the budget bounds peak memory no matter
/// how many uploads arrive in parallel: extra requests wait their turn instead
/// of allocating at the same time. One gate per budget value, shared by every
/// decode path (receipts, product, impression and haul photos).
/// </summary>
public sealed class ImageDecodeGate
{
    private static readonly ConcurrentDictionary<long, ImageDecodeGate> Gates = new();

    private readonly object _sync = new();
    private readonly long _capacity;
    private long _inUse;

    private ImageDecodeGate(long capacity) => _capacity = capacity;

    /// <summary>The shared gate for this budget (pixels in flight).</summary>
    public static ImageDecodeGate ForBudget(long capacityPixels) =>
        Gates.GetOrAdd(Math.Max(1, capacityPixels), static capacity => new ImageDecodeGate(capacity));

    public long PixelsInUse
    {
        get
        {
            lock (_sync)
            {
                return _inUse;
            }
        }
    }

    /// <summary>
    /// Waits until <paramref name="pixels"/> fit in the budget (a request larger
    /// than the whole budget runs alone). Returns null when the wait times out.
    /// </summary>
    public IDisposable? TryEnter(long pixels, TimeSpan timeout)
    {
        var cost = Math.Clamp(pixels, 1, _capacity);
        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        lock (_sync)
        {
            while (_inUse + cost > _capacity)
            {
                var left = deadline - Environment.TickCount64;
                if (left <= 0)
                {
                    return null;
                }

                Monitor.Wait(_sync, (int)Math.Min(left, int.MaxValue));
            }

            _inUse += cost;
        }

        return new Lease(this, cost);
    }

    private void Release(long cost)
    {
        lock (_sync)
        {
            _inUse -= cost;
            Monitor.PulseAll(_sync);
        }
    }

    private sealed class Lease(ImageDecodeGate gate, long cost) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                gate.Release(cost);
            }
        }
    }
}
