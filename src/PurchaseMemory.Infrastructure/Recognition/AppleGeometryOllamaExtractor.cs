using Microsoft.Extensions.Logging;
using PurchaseMemory.Application.Receipts;

namespace PurchaseMemory.Infrastructure.Recognition;

/// <summary>
/// The "Apple reads, Ollama structures" pipeline: Apple Vision OCR produces rows
/// with coordinates, and a text model turns those rows into a receipt draft.
///
/// This is a third, independent route, not a mode of the existing ones.
/// <c>Mode = Hybrid</c> on <see cref="OllamaStructuredVisionProvider"/> means
/// "Ollama OCR + Ollama parser"; the Apple pipeline means "Apple OCR + Apple
/// Foundation Models". Neither can express this combination, and the existing
/// routes stay untouched so <c>qwen3-vl:8b-instruct</c> remains a working
/// fallback.
/// </summary>
public sealed partial class AppleGeometryOllamaExtractor : IReceiptRecognitionPipeline
{
    public const string PipelineId = "apple-ollama";

    private readonly IReceiptOcrRowReader _rowReader;
    private readonly IReceiptRowsAnalysisWorkflow _workflow;
    private readonly IStructuredTextProviderResolver _providerResolver;
    private readonly ILogger<AppleGeometryOllamaExtractor> _logger;

    public AppleGeometryOllamaExtractor(
        IReceiptOcrRowReader rowReader,
        IReceiptRowsAnalysisWorkflow workflow,
        IStructuredTextProviderResolver providerResolver,
        ILogger<AppleGeometryOllamaExtractor> logger)
    {
        _rowReader = rowReader;
        _workflow = workflow;
        _providerResolver = providerResolver;
        _logger = logger;
    }

    public string Id => PipelineId;

    public async Task<ReceiptExtractionResult> ExtractAsync(
        IReadOnlyList<ReceiptImageData> images,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(images);
        if (images.Count == 0)
        {
            throw new ArgumentException("Receipt images cannot be empty.", nameof(images));
        }

        var correlationId = Guid.NewGuid().ToString("N");
        var rowCount = 0;
        var pages = new List<ReceiptOcrPage>(images.Count);
        foreach (var image in images)
        {
            var page = await _rowReader.ReadAsync(image, cancellationToken);
            rowCount += page.Rows.Count;
            pages.Add(page);
        }

        try
        {
            var result = await _workflow.AnalyzeAsync(
                new ReceiptRowsAnalysisRequest(
                    correlationId,
                    pages,
                    OllamaStructuredTextProvider.ProviderId),
                cancellationToken);
            AnalysisCompleted(
                result.Metadata.Model,
                rowCount,
                result.Metadata.DurationMilliseconds,
                result.Extraction.Items.Count,
                correlationId);
            return result.Extraction;
        }
        catch (ReceiptStructuredOutputException exception)
        {
            InvalidStructuredOutput(exception.ValidationErrors.Count, correlationId);
            throw new ReceiptExtractionException(
                "The receipt parser model returned an invalid receipt structure. "
                + "Retry or configure another parser model.",
                exception);
        }
    }

    public async Task<ReceiptRecognitionStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var ocrStatus = await _rowReader.GetStatusAsync(cancellationToken);
        var parserStatus = await _providerResolver
            .Resolve(OllamaStructuredTextProvider.ProviderId)
            .GetStatusAsync(cancellationToken);

        var components = ocrStatus.Models.Concat(parserStatus.Models).ToArray();
        var messages = new[] { ocrStatus.Message, parserStatus.Message }
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .ToArray();

        return new ReceiptRecognitionStatus(
            ocrStatus.Available && parserStatus.Available,
            "Apple Vision OCR rows → Ollama text parser",
            components,
            messages.Length == 0 ? null : string.Join(' ', messages));
    }

    [LoggerMessage(
        EventId = 2601,
        Level = LogLevel.Information,
        Message = "Apple OCR rows parsed by {Model}: {RowCount} rows in {DurationMs} ms produced {ItemCount} items; correlation {CorrelationId}")]
    private partial void AnalysisCompleted(
        string model,
        int rowCount,
        long durationMs,
        int itemCount,
        string correlationId);

    [LoggerMessage(
        EventId = 2602,
        Level = LogLevel.Warning,
        Message = "The receipt parser model returned invalid structure after repair; {ValidationErrorCount} validation errors; correlation {CorrelationId}")]
    private partial void InvalidStructuredOutput(int validationErrorCount, string correlationId);
}
