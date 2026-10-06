namespace PurchaseMemory.Application.Receipts;

/// <summary>
/// One printed receipt line as OCR geometry reconstructed it: the vertical
/// position on a 0..1000 page scale (0 = top) and the text pieces left to right.
/// This is the hand-off format between an OCR stage that knows coordinates and a
/// text model that does not.
/// </summary>
public sealed record ReceiptOcrRow(int Y, IReadOnlyList<string> Cells);

/// <summary>
/// Rows of a single receipt photo plus the diagnostics the geometry stage
/// produced. The counters are quality signals, not results: a page with several
/// rows holding more than one price cell was probably deskewed badly.
/// </summary>
public sealed record ReceiptOcrPage(
    IReadOnlyList<ReceiptOcrRow> Rows,
    int FragmentCount,
    double Skew,
    int RowsWithSinglePriceCell,
    int RowsWithMultiplePriceCells);

/// <summary>
/// Reads a receipt photo as positioned OCR rows. Deliberately separate from
/// <see cref="IStructuredVisionProvider"/>: this stage never sees a prompt and
/// never talks to a language model.
/// </summary>
public interface IReceiptOcrRowReader
{
    /// <summary>A stable, case-insensitive configuration identifier.</summary>
    string Id { get; }

    Task<ReceiptOcrPage> ReadAsync(ReceiptImageData image, CancellationToken cancellationToken);

    Task<ReceiptRecognitionStatus> GetStatusAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Provider-neutral request for a text-only structured completion. It carries no
/// images: the receipt has already been read by an OCR stage.
/// </summary>
public sealed record StructuredTextRequest(
    string Prompt,
    string JsonSchema,
    string PromptVersion,
    string SchemaVersion,
    string CorrelationId);

public sealed record StructuredTextResponse(
    string RawJson,
    string Provider,
    string Model,
    string? FinishReason,
    long DurationMilliseconds);

public sealed record StructuredTextProviderStatus(
    bool Available,
    string Provider,
    IReadOnlyList<string> Models,
    string? Message);

public interface IStructuredTextProvider
{
    /// <summary>A stable, case-insensitive configuration identifier.</summary>
    string Id { get; }

    Task<StructuredTextResponse> GenerateAsync(
        StructuredTextRequest request,
        CancellationToken cancellationToken);

    Task<StructuredTextProviderStatus> GetStatusAsync(CancellationToken cancellationToken);
}
