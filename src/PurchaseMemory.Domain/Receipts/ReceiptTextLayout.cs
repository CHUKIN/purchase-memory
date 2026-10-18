namespace PurchaseMemory.Domain.Receipts;

/// <summary>A printed word with its outline in photo pixels (four corners, reading order).</summary>
public sealed record ReceiptTextWord(string Text, IReadOnlyList<double> Polygon);

/// <summary>A printed line as the OCR service grouped it: its text, outline and words.</summary>
public sealed record ReceiptTextLine(string Text, IReadOnlyList<double> Polygon, IReadOnlyList<ReceiptTextWord> Words);

/// <summary>
/// What an OCR service (Azure Document Intelligence prebuilt-read) read on the
/// single photo of a receipt: lines and words with pixel outlines, plus the
/// page size that turns pixels into the photo fractions every highlight uses.
/// Kept apart from <see cref="ReceiptCapture"/> (its own table) so lists of
/// receipts never load it; it is only read to (re)match lines after an edit.
/// </summary>
public sealed class ReceiptTextLayout
{
    private ReceiptTextLayout()
    {
        // Required by Entity Framework Core.
    }

    private ReceiptTextLayout(
        Guid receiptCaptureId,
        string source,
        double pageWidth,
        double pageHeight,
        IReadOnlyList<ReceiptTextLine> lines,
        DateTimeOffset createdAt)
    {
        if (receiptCaptureId == Guid.Empty)
        {
            throw new ArgumentException("Receipt identifier cannot be empty.", nameof(receiptCaptureId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (source.Trim().Length > 100)
        {
            throw new ArgumentException("Layout source cannot exceed 100 characters.", nameof(source));
        }

        if (!double.IsFinite(pageWidth) || !double.IsFinite(pageHeight) || pageWidth < 1 || pageHeight < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(pageWidth), "The page size must be positive.");
        }

        ArgumentNullException.ThrowIfNull(lines);
        ReceiptCaptureId = receiptCaptureId;
        Source = source.Trim();
        PageWidth = pageWidth;
        PageHeight = pageHeight;
        Lines = lines;
        CreatedAt = createdAt;
    }

    public Guid ReceiptCaptureId { get; private set; }

    /// <summary>Service, model and API version that produced the layout, e.g. <c>azure:prebuilt-read:2024-11-30</c>.</summary>
    public string Source { get; private set; } = string.Empty;

    public double PageWidth { get; private set; }

    public double PageHeight { get; private set; }

    public IReadOnlyList<ReceiptTextLine> Lines { get; private set; } = [];

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Takes over a newer reading of the same receipt.</summary>
    public void ReplaceWith(ReceiptTextLayout newer)
    {
        ArgumentNullException.ThrowIfNull(newer);
        if (newer.ReceiptCaptureId != ReceiptCaptureId)
        {
            throw new ArgumentException("A layout can only be replaced by one of the same receipt.", nameof(newer));
        }

        Source = newer.Source;
        PageWidth = newer.PageWidth;
        PageHeight = newer.PageHeight;
        Lines = newer.Lines;
        CreatedAt = newer.CreatedAt;
    }

    public static ReceiptTextLayout Create(
        Guid receiptCaptureId,
        string source,
        double pageWidth,
        double pageHeight,
        IReadOnlyList<ReceiptTextLine> lines,
        DateTimeOffset createdAt) =>
        new(receiptCaptureId, source, pageWidth, pageHeight, lines, createdAt);
}
