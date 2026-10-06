using Microsoft.Extensions.Logging;
using PurchaseMemory.Application.Receipts;

namespace PurchaseMemory.Infrastructure.Recognition;

/// <summary>
/// Compatibility facade for the existing receipt processor and legacy pipeline
/// router. All receipt interpretation is delegated to the shared workflow.
/// </summary>
public sealed partial class OllamaReceiptExtractor : IReceiptRecognitionPipeline
{
    private readonly IReceiptAnalysisWorkflow _workflow;
    private readonly IStructuredVisionProviderResolver _providerResolver;
    private readonly ILogger<OllamaReceiptExtractor> _logger;

    public OllamaReceiptExtractor(
        IReceiptAnalysisWorkflow workflow,
        IStructuredVisionProviderResolver providerResolver,
        ILogger<OllamaReceiptExtractor> logger)
    {
        _workflow = workflow;
        _providerResolver = providerResolver;
        _logger = logger;
    }

    public string Id => "ollama";

    public async Task<ReceiptExtractionResult> ExtractAsync(
        IReadOnlyList<ReceiptImageData> images,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(images);
        try
        {
            var correlationId = Guid.NewGuid().ToString("N");
            var result = await _workflow.AnalyzeAsync(
                new ReceiptAnalysisRequest(
                    Guid.NewGuid(),
                    correlationId,
                    images,
                    Id),
                cancellationToken);
            AnalysisCompleted(
                result.Metadata.Provider,
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
                "The local model returned an invalid receipt structure. Retry or choose another model.",
                exception);
        }
    }

    public async Task<ReceiptRecognitionStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var status = await _providerResolver.Resolve(Id).GetStatusAsync(cancellationToken);
        return new ReceiptRecognitionStatus(
            status.Available,
            "Ollama structured receipt workflow",
            status.Models,
            status.Message);
    }

    [LoggerMessage(
        EventId = 2111,
        Level = LogLevel.Information,
        Message = "Receipt workflow completed with provider {Provider} and model {Model} in {DurationMs} ms; {ItemCount} items; correlation {CorrelationId}")]
    private partial void AnalysisCompleted(
        string provider,
        string model,
        long durationMs,
        int itemCount,
        string correlationId);

    [LoggerMessage(
        EventId = 2112,
        Level = LogLevel.Warning,
        Message = "Receipt workflow rejected provider output after repair; {ValidationErrorCount} validation errors")]
    private partial void InvalidStructuredOutput(int validationErrorCount);
}
