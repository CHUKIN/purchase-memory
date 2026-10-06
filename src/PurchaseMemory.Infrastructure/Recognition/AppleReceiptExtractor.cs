using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PurchaseMemory.Application.Receipts;

namespace PurchaseMemory.Infrastructure.Recognition;

public sealed partial class AppleReceiptExtractor : IReceiptRecognitionPipeline
{
    private static readonly string[] PipelineComponents =
    [
        "Apple Vision OCR",
        "Apple Translation pl-PL → en-US",
        "Apple Foundation Models"
    ];

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ReceiptRecognitionOptions _options;
    private readonly ILogger<AppleReceiptExtractor> _logger;

    public string Id => "apple";

    public AppleReceiptExtractor(
        IOptions<ReceiptRecognitionOptions> options,
        ILogger<AppleReceiptExtractor> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ReceiptExtractionResult> ExtractAsync(
        IReadOnlyList<ReceiptImageData> images,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(images);
        if (images.Count == 0)
        {
            throw new ArgumentException("Receipt images cannot be empty.", nameof(images));
        }

        var results = new List<ReceiptExtractionResult>(images.Count);
        foreach (var image in images)
        {
            using var stream = new MemoryStream(image.Content.ToArray(), writable: false);
            results.Add(await ExtractSingleAsync(stream, image.ContentType, cancellationToken));
        }

        return new ReceiptExtractionResult(
            results.Select(result => result.MerchantName).FirstOrDefault(value => value is not null),
            results.Select(result => result.PurchasedAt).FirstOrDefault(value => value is not null),
            results.Select(result => result.Currency).LastOrDefault(value => value is not null),
            results.Select(result => result.ReceiptTotal).LastOrDefault(value => value is not null),
            results.SelectMany(result => result.Items).ToList(),
            results[0].ExtractorName,
            results[0].ModelName);
    }

    private async Task<ReceiptExtractionResult> ExtractSingleAsync(
        Stream image,
        string contentType,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

        EnsureMacOsAndSidecar();
        var temporaryImagePath = Path.Combine(
            Path.GetTempPath(),
            $"purchase-memory-{Guid.NewGuid():N}{ExtensionFor(contentType)}");

        try
        {
            await using (var temporaryImage = new FileStream(
                temporaryImagePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81_920,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await image.CopyToAsync(temporaryImage, cancellationToken);
            }

            var processResult = await AppleSidecarProcess.RunAsync(
                ResolveSidecarPath(),
                ["extract", temporaryImagePath],
                TimeSpan.FromSeconds(_options.AppleRequestTimeoutSeconds),
                cancellationToken);
            var response = DeserializeExtraction(processResult);
            var validation = AppleReceiptResultValidator.ValidateAndCreate(
                response.Receipt
                ?? throw new ReceiptExtractionException("Apple sidecar returned no receipt payload."));

            var diagnostics = response.Diagnostics;
            RecognitionCompleted(
                diagnostics?.TotalDurationMs ?? 0,
                diagnostics?.OcrDurationMs ?? 0,
                diagnostics?.TranslationDurationMs ?? 0,
                diagnostics?.FoundationModelDurationMs ?? 0,
                validation.Extraction.Items.Count,
                validation.ArithmeticMatches,
                validation.CorrectedMoneyValues);

            return validation.Extraction;
        }
        catch (ReceiptExtractionException)
        {
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ReceiptExtractionException(
                "Apple on-device receipt recognition timed out. You can retry processing.");
        }
        catch (JsonException exception)
        {
            InvalidSidecarJson(exception);
            throw new ReceiptExtractionException(
                "Apple receipt sidecar returned invalid structured data.",
                exception);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            SidecarFailed(exception);
            throw new ReceiptExtractionException(
                "Apple on-device receipt recognition failed. Ollama can be used as a fallback.",
                exception);
        }
        finally
        {
            TryDeleteTemporaryImage(temporaryImagePath);
        }
    }

    public async Task<ReceiptRecognitionStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return Unavailable("Apple receipt recognition requires macOS 26.4 or later.");
        }

        var sidecarPath = ResolveSidecarPath();
        if (!File.Exists(sidecarPath))
        {
            return Unavailable($"Build the Apple sidecar first: {sidecarPath}");
        }

        try
        {
            var result = await AppleSidecarProcess.RunAsync(
                ResolveSidecarPath(),
                ["status"],
                TimeSpan.FromSeconds(Math.Min(_options.AppleRequestTimeoutSeconds, 10)),
                cancellationToken);
            if (result.ExitCode != 0)
            {
                return Unavailable(AppleSidecarProcess.ReadError(result));
            }

            var status = JsonSerializer.Deserialize<AppleSidecarStatus>(result.StandardOutput, JsonOptions);
            return status?.Available == true
                ? new ReceiptRecognitionStatus(true, "Apple on-device hybrid", PipelineComponents, null)
                : Unavailable(status?.Message ?? "Apple Intelligence is not ready on this Mac.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            StatusCheckFailed(exception);
            return Unavailable("Cannot start the Apple receipt sidecar.");
        }
    }

    private static AppleSidecarExtraction DeserializeExtraction(SidecarProcessResult processResult)
    {
        if (processResult.ExitCode != 0)
        {
            throw new ReceiptExtractionException(AppleSidecarProcess.ReadError(processResult));
        }

        return JsonSerializer.Deserialize<AppleSidecarExtraction>(processResult.StandardOutput, JsonOptions)
            ?? throw new ReceiptExtractionException("Apple sidecar returned an empty response.");
    }

    private void EnsureMacOsAndSidecar()
    {
        if (!OperatingSystem.IsMacOSVersionAtLeast(26, 4))
        {
            throw new ReceiptExtractionException(
                "Apple on-device recognition requires macOS 26.4 or later.");
        }

        var sidecarPath = ResolveSidecarPath();
        if (!File.Exists(sidecarPath))
        {
            throw new ReceiptExtractionException(
                $"Apple receipt sidecar is missing at {sidecarPath}. Run scripts/build-apple-sidecar.sh.");
        }
    }

    private string ResolveSidecarPath() => AppleSidecarProcess.ResolvePath(_options.AppleSidecarPath);

    private static string ExtensionFor(string contentType)
    {
        return contentType.Split(';', 2)[0].Trim().ToLowerInvariant() switch
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

    private static ReceiptRecognitionStatus Unavailable(string message)
    {
        return new ReceiptRecognitionStatus(false, "Apple on-device hybrid", PipelineComponents, message);
    }

    [LoggerMessage(
        EventId = 2201,
        Level = LogLevel.Information,
        Message = "Apple receipt recognition completed in {TotalDurationMs} ms (OCR {OcrDurationMs} ms, translation {TranslationDurationMs} ms, Foundation Models {FoundationModelDurationMs} ms); {ItemCount} items, arithmetic valid: {ArithmeticMatches}, corrected money values: {CorrectedMoneyValues}")]
    private partial void RecognitionCompleted(
        int totalDurationMs,
        int ocrDurationMs,
        int translationDurationMs,
        int foundationModelDurationMs,
        int itemCount,
        bool arithmeticMatches,
        int correctedMoneyValues);

    [LoggerMessage(EventId = 2202, Level = LogLevel.Warning, Message = "Apple receipt sidecar returned invalid JSON")]
    private partial void InvalidSidecarJson(Exception exception);

    [LoggerMessage(EventId = 2203, Level = LogLevel.Warning, Message = "Apple receipt sidecar failed")]
    private partial void SidecarFailed(Exception exception);

    [LoggerMessage(EventId = 2204, Level = LogLevel.Debug, Message = "Apple receipt status check failed")]
    private partial void StatusCheckFailed(Exception exception);

    [LoggerMessage(EventId = 2205, Level = LogLevel.Warning, Message = "Could not delete temporary receipt image {Path}")]
    private partial void TemporaryImageDeleteFailed(string path, Exception exception);
}

internal sealed class AppleSidecarExtraction
{
    public int SchemaVersion { get; init; }

    public AppleSidecarReceipt? Receipt { get; init; }

    public AppleSidecarDiagnostics? Diagnostics { get; init; }
}

internal sealed class AppleSidecarReceipt
{
    public string? MerchantName { get; init; }

    public string? PurchasedAt { get; init; }

    public string? Currency { get; init; }

    public decimal? ReceiptTotal { get; init; }

    public IReadOnlyList<AppleSidecarReceiptItem>? Items { get; init; }
}

internal sealed class AppleSidecarReceiptItem
{
    public string? Name { get; init; }

    public string? NameRowId { get; init; }

    public decimal? Quantity { get; init; }

    public string? Unit { get; init; }

    public decimal? UnitPrice { get; init; }

    public decimal? GrossLineTotal { get; init; }

    public decimal? Discount { get; init; }

    public decimal? LineTotal { get; init; }
}

internal sealed class AppleSidecarDiagnostics
{
    public int OcrDurationMs { get; init; }

    public int TranslationDurationMs { get; init; }

    public int FoundationModelDurationMs { get; init; }

    public int TotalDurationMs { get; init; }
}

internal sealed class AppleSidecarStatus
{
    public bool Available { get; init; }

    public string? Message { get; init; }
}
