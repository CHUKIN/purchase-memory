using Microsoft.Extensions.Logging;
using PurchaseMemory.Application.Receipts;

namespace PurchaseMemory.Infrastructure.Recognition;

/// <summary>
/// Compatibility facade that lets the existing router select OpenAI without
/// changing the receipt workflow, REST API, processing worker, or PWA.
/// </summary>
public sealed partial class OpenAiReceiptExtractor : IReceiptRecognitionPipeline
{
    private readonly IReceiptAnalysisWorkflow _workflow;
    private readonly IStructuredVisionProviderResolver _providerResolver;
    private readonly ILogger<OpenAiReceiptExtractor> _logger;

    public OpenAiReceiptExtractor(
        IReceiptAnalysisWorkflow workflow,
        IStructuredVisionProviderResolver providerResolver,
        ILogger<OpenAiReceiptExtractor> logger)
    {
        _workflow = workflow;
        _providerResolver = providerResolver;
        _logger = logger;
    }

    public string Id => OpenAiStructuredVisionProvider.ProviderId;

    public async Task<ReceiptExtractionResult> ExtractAsync(
        IReadOnlyList<ReceiptImageData> images,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(images);
        var correlationId = Guid.NewGuid().ToString("N");
        try
        {
            var result = await _workflow.AnalyzeAsync(
                new ReceiptAnalysisRequest(
                    Guid.NewGuid(),
                    correlationId,
                    images,
                    Id),
                cancellationToken);
            AnalysisCompleted(
                result.Metadata.Model,
                result.Metadata.DurationMilliseconds,
                result.Extraction.Items.Count,
                correlationId);
            return result.Extraction;
        }
        catch (ReceiptStructuredOutputException exception)
        {
            InvalidStructuredOutput(exception.ValidationErrors.Count);
            throw new ReceiptExtractionException(
                "OpenAI returned an invalid receipt structure. Retry or choose another provider.",
                exception);
        }
    }

    public async Task<ReceiptRecognitionStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var status = await _providerResolver.Resolve(Id).GetStatusAsync(cancellationToken);
        return new ReceiptRecognitionStatus(
            status.Available,
            "OpenAI structured receipt workflow",
            status.Models,
            status.Message);
    }

    [LoggerMessage(
        EventId = 2211,
        Level = LogLevel.Information,
        Message = "OpenAI receipt workflow completed with model {Model} in {DurationMs} ms; {ItemCount} items; correlation {CorrelationId}")]
    private partial void AnalysisCompleted(
        string model,
        long durationMs,
        int itemCount,
        string correlationId);

    [LoggerMessage(
        EventId = 2212,
        Level = LogLevel.Warning,
        Message = "OpenAI receipt workflow rejected provider output after repair; {ValidationErrorCount} validation errors")]
    private partial void InvalidStructuredOutput(int validationErrorCount);
}
