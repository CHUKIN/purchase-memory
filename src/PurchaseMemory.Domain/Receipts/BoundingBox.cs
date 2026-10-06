namespace PurchaseMemory.Domain.Receipts;

/// <summary>
/// Where a recognized value sits on the receipt photo, in relative fractions of
/// the image size (0.0–1.0) so the same box works for any capture resolution.
/// A box is best-effort: recognition models are not reliable at coordinates, so
/// everything that is not clearly usable is dropped instead of being shown.
/// </summary>
public sealed record BoundingBox
{
    /// <summary>A box that is this close to the full frame says nothing about where to look.</summary>
    private const double MaximumArea = 0.9;

    /// <summary>Below this a crop would be an unreadable sliver, so it is treated as noise.</summary>
    private const double MinimumWidth = 0.01;

    private const double MinimumHeight = 0.002;

    /// <summary>Absorbs the rounding a model applies when it emits 3-4 decimals.</summary>
    private const double EdgeTolerance = 0.005;

    private BoundingBox(double x, double y, double width, double height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    /// <summary>Left edge as a fraction of the image width.</summary>
    public double X { get; private init; }

    /// <summary>Top edge as a fraction of the image height.</summary>
    public double Y { get; private init; }

    public double Width { get; private init; }

    public double Height { get; private init; }

    /// <summary>
    /// Returns a usable box, or <c>null</c> when any coordinate is missing,
    /// non-finite, outside the image, degenerate or so large that it points at
    /// the whole receipt rather than at one value.
    /// </summary>
    public static BoundingBox? TryCreate(double? x, double? y, double? width, double? height)
    {
        if (x is not { } left || y is not { } top || width is not { } boxWidth || height is not { } boxHeight)
        {
            return null;
        }

        if (!double.IsFinite(left) || !double.IsFinite(top)
            || !double.IsFinite(boxWidth) || !double.IsFinite(boxHeight))
        {
            return null;
        }

        if (left < -EdgeTolerance || top < -EdgeTolerance)
        {
            return null;
        }

        if (boxWidth < MinimumWidth || boxHeight < MinimumHeight)
        {
            return null;
        }

        left = Math.Max(0, left);
        top = Math.Max(0, top);
        if (left + boxWidth > 1 + EdgeTolerance || top + boxHeight > 1 + EdgeTolerance)
        {
            return null;
        }

        boxWidth = Math.Min(boxWidth, 1 - left);
        boxHeight = Math.Min(boxHeight, 1 - top);
        if (boxWidth < MinimumWidth || boxHeight < MinimumHeight || boxWidth * boxHeight > MaximumArea)
        {
            return null;
        }

        return new BoundingBox(left, top, boxWidth, boxHeight);
    }
}
