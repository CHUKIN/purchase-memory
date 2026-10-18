namespace PurchaseMemory.Domain.Receipts;

/// <summary>
/// One closed outline around printed text on the receipt photo, as flat
/// <c>x0, y0, x1, y1, …</c> pairs in fractions of the image size — the same
/// coordinate space as <see cref="BoundingBox"/>. A line's highlight is the
/// union of several such outlines (one per pair of neighbouring words), so it
/// follows a tilted or slightly bent printed line.
/// </summary>
public sealed record TextPolygon
{
    /// <summary>Points may stick out a little where padding meets the photo edge; they are clamped.</summary>
    private const double EdgeTolerance = 0.05;

    private const int MaximumPoints = 64;

    private TextPolygon(IReadOnlyList<double> points)
    {
        Points = points;
    }

    /// <summary>Flat x/y pairs, at least three points.</summary>
    public IReadOnlyList<double> Points { get; }

    public int PointCount => Points.Count / 2;

    /// <summary>
    /// Returns a usable outline, or <c>null</c> when the points are too few,
    /// too many, not finite or clearly outside the photo. Coordinates are
    /// clamped to the photo and rounded to 4 decimals (a fraction of a pixel on
    /// any phone photo), which keeps the stored JSON small.
    /// </summary>
    public static TextPolygon? TryCreate(IReadOnlyList<double>? points)
    {
        if (points is null || points.Count % 2 != 0 || points.Count < 6 || points.Count > MaximumPoints * 2)
        {
            return null;
        }

        var clamped = new double[points.Count];
        for (var index = 0; index < points.Count; index++)
        {
            var value = points[index];
            if (!double.IsFinite(value) || value < -EdgeTolerance || value > 1 + EdgeTolerance)
            {
                return null;
            }

            clamped[index] = Math.Round(Math.Clamp(value, 0, 1), 4);
        }

        return new TextPolygon(clamped);
    }

    public bool Equals(TextPolygon? other) =>
        other is not null && Points.SequenceEqual(other.Points);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var value in Points)
        {
            hash.Add(value);
        }

        return hash.ToHashCode();
    }
}

/// <summary>Where the highlight of a receipt line comes from.</summary>
public enum ReceiptBoxSource
{
    /// <summary>The rectangle the recognition model (Claude Haiku) returned with the line.</summary>
    Recognition = 0,

    /// <summary>Word outlines from Azure Document Intelligence (prebuilt-read) matched to the line.</summary>
    Azure = 1
}

/// <summary>
/// Progress of the optional text-location step (Azure word outlines) for a
/// receipt. It runs in the background after recognition and never blocks
/// review or saving.
/// </summary>
public enum ReceiptTextLocationStatus
{
    /// <summary>Not tried yet: a new receipt, or one from before the feature (backfill).</summary>
    NotRequested = 0,

    /// <summary>The text layout is stored; lines are matched against it.</summary>
    Located = 1,

    /// <summary>Will not be tried: several photos, an unsupported photo, or Azure refused it for good.</summary>
    Unavailable = 2,

    /// <summary>The last try failed for a passing reason; retried a limited number of times.</summary>
    Failed = 3
}
