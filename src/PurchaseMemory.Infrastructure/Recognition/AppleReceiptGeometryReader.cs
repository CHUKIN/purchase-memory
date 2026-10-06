using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PurchaseMemory.Application.Receipts;

namespace PurchaseMemory.Infrastructure.Recognition;

/// <summary>
/// Reads a receipt photo as positioned OCR rows through the Apple sidecar's
/// <c>geometry</c> command.
///
/// Its availability is deliberately checked differently from
/// <see cref="AppleReceiptExtractor"/>: <c>geometry</c> is Vision OCR only, with
/// no Foundation model and no translation, so it keeps working on a Mac where
/// Apple Intelligence itself is unavailable. Asking the sidecar's <c>status</c>
/// command here would disable a working OCR stage for an unrelated reason.
/// </summary>
public sealed partial class AppleReceiptGeometryReader : IReceiptOcrRowReader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] Components = ["Apple Vision OCR", "Row geometry and skew correction"];

    private readonly ReceiptRecognitionOptions _options;
    private readonly ILogger<AppleReceiptGeometryReader> _logger;

    public AppleReceiptGeometryReader(
        IOptions<ReceiptRecognitionOptions> options,
        ILogger<AppleReceiptGeometryReader> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public string Id => "apple-geometry";

    public async Task<ReceiptOcrPage> ReadAsync(
        ReceiptImageData image,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.Content.IsEmpty)
        {
            throw new ArgumentException("Receipt image cannot be empty.", nameof(image));
        }

        var sidecarPath = EnsureMacOsAndSidecar();
        var temporaryImagePath = Path.Combine(
            Path.GetTempPath(),
            $"purchase-memory-geometry-{Guid.NewGuid():N}{ExtensionFor(image.ContentType)}");

        try
        {
            await File.WriteAllBytesAsync(temporaryImagePath, image.Content, cancellationToken);

            var processResult = await AppleSidecarProcess.RunAsync(
                sidecarPath,
                ["geometry", temporaryImagePath],
                TimeSpan.FromSeconds(_options.AppleRequestTimeoutSeconds),
                cancellationToken);
            if (processResult.ExitCode != 0)
            {
                throw new ReceiptExtractionException(AppleSidecarProcess.ReadError(processResult));
            }

            var output = JsonSerializer.Deserialize<GeometryOutput>(processResult.StandardOutput, JsonOptions)
                ?? throw new ReceiptExtractionException("Apple sidecar returned an empty geometry response.");
            var rows = (output.Rows ?? [])
                .Select(row => new ReceiptOcrRow(
                    row.Y,
                    (row.Cells ?? []).Select(cell => cell.Text ?? string.Empty).ToArray()))
                .Where(row => row.Cells.Any(cell => !string.IsNullOrWhiteSpace(cell)))
                .ToArray();
            if (rows.Length == 0)
            {
                throw new ReceiptExtractionException(
                    "Apple Vision did not find readable rows in this receipt photo.");
            }

            var diagnostics = output.Diagnostics;
            GeometryCompleted(
                diagnostics?.FragmentCount ?? 0,
                rows.Length,
                diagnostics?.Skew ?? 0,
                diagnostics?.RowsWithSinglePriceCell ?? 0,
                diagnostics?.RowsWithMultiplePriceCells ?? 0,
                diagnostics?.OcrDurationMs ?? 0);
            if (diagnostics?.SkewReachedSearchBoundary == true)
            {
                SkewReachedSearchBoundary(diagnostics.Skew);
            }

            return new ReceiptOcrPage(
                rows,
                diagnostics?.FragmentCount ?? rows.Sum(row => row.Cells.Count),
                diagnostics?.Skew ?? 0,
                diagnostics?.RowsWithSinglePriceCell ?? 0,
                diagnostics?.RowsWithMultiplePriceCells ?? 0);
        }
        catch (ReceiptExtractionException)
        {
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ReceiptExtractionException(
                "Apple Vision OCR timed out on this receipt photo. You can retry processing.");
        }
        catch (JsonException exception)
        {
            InvalidGeometryJson(exception);
            throw new ReceiptExtractionException(
                "Apple receipt sidecar returned invalid geometry data.",
                exception);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            GeometryFailed(exception);
            throw new ReceiptExtractionException(
                "Apple Vision OCR failed on this receipt photo.",
                exception);
        }
        finally
        {
            TryDeleteTemporaryImage(temporaryImagePath);
        }
    }

    public Task<ReceiptRecognitionStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsMacOSVersionAtLeast(26, 4))
        {
            return Task.FromResult(Unavailable("Apple Vision receipt OCR requires macOS 26.4 or later."));
        }

        var sidecarPath = AppleSidecarProcess.ResolvePath(_options.AppleSidecarPath);
        return Task.FromResult(File.Exists(sidecarPath)
            ? new ReceiptRecognitionStatus(true, "Apple Vision OCR rows", Components, null)
            : Unavailable($"Build the Apple sidecar first: {sidecarPath}"));
    }

    private string EnsureMacOsAndSidecar()
    {
        if (!OperatingSystem.IsMacOSVersionAtLeast(26, 4))
        {
            throw new ReceiptExtractionException(
                "Apple Vision receipt OCR requires macOS 26.4 or later.");
        }

        var sidecarPath = AppleSidecarProcess.ResolvePath(_options.AppleSidecarPath);
        return File.Exists(sidecarPath)
            ? sidecarPath
            : throw new ReceiptExtractionException(
                $"Apple receipt sidecar is missing at {sidecarPath}. Run scripts/build-apple-sidecar.sh.");
    }

    private static ReceiptRecognitionStatus Unavailable(string message) =>
        new(false, "Apple Vision OCR rows", Components, message);

    private static string ExtensionFor(string contentType)
    {
        return (contentType ?? string.Empty).Split(';', 2)[0].Trim().ToLowerInvariant() switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => ".image"
        };
    }

    private void TryDeleteTemporaryImage(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException exception)
        {
            TemporaryImageDeleteFailed(path, exception);
        }
    }

    private sealed class GeometryOutput
    {
        public int SchemaVersion { get; init; }
        public IReadOnlyList<GeometryRow>? Rows { get; init; }
        public GeometryDiagnostics? Diagnostics { get; init; }
    }

    private sealed class GeometryRow
    {
        public int Y { get; init; }
        public IReadOnlyList<GeometryCell>? Cells { get; init; }
    }

    private sealed class GeometryCell
    {
        public string? Text { get; init; }
    }

    private sealed class GeometryDiagnostics
    {
        public int FragmentCount { get; init; }
        public int RowCount { get; init; }
        public double Skew { get; init; }
        public bool SkewReachedSearchBoundary { get; init; }
        public int RowsWithSinglePriceCell { get; init; }
        public int RowsWithMultiplePriceCells { get; init; }
        public int OcrDurationMs { get; init; }
    }

    [LoggerMessage(
        EventId = 2401,
        Level = LogLevel.Information,
        Message = "Apple Vision geometry read {FragmentCount} fragments into {RowCount} rows in {OcrDurationMs} ms (skew {Skew}, rows with one price cell {RowsWithSinglePriceCell}, with several {RowsWithMultiplePriceCells})")]
    private partial void GeometryCompleted(
        int fragmentCount,
        int rowCount,
        double skew,
        int rowsWithSinglePriceCell,
        int rowsWithMultiplePriceCells,
        int ocrDurationMs);

    [LoggerMessage(
        EventId = 2402,
        Level = LogLevel.Warning,
        Message = "Apple Vision skew search stopped at its boundary ({Skew}); the photo may be tilted more than the search can express")]
    private partial void SkewReachedSearchBoundary(double skew);

    [LoggerMessage(EventId = 2403, Level = LogLevel.Warning, Message = "Apple receipt sidecar returned invalid geometry JSON")]
    private partial void InvalidGeometryJson(Exception exception);

    [LoggerMessage(EventId = 2404, Level = LogLevel.Warning, Message = "Apple Vision geometry read failed")]
    private partial void GeometryFailed(Exception exception);

    [LoggerMessage(EventId = 2405, Level = LogLevel.Warning, Message = "Could not delete temporary receipt image {Path}")]
    private partial void TemporaryImageDeleteFailed(string path, Exception exception);
}
