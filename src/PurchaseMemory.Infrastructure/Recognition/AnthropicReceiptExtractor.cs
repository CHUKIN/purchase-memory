using Microsoft.Extensions.Logging;
using PurchaseMemory.Application.Receipts;

namespace PurchaseMemory.Infrastructure.Recognition;

/// <summary>
/// The <c>anthropic</c> receipt pipeline: the shared receipt workflow (prompt,
/// schema, parser, one repair retry) over <see cref="AnthropicStructuredVisionProvider"/>.
/// Output that is still invalid after the repair retry becomes a
/// <see cref="ReceiptExtractionException"/> so the router tries the fallback.
/// </summary>
public sealed partial class AnthropicReceiptExtractor : IReceiptRecognitionPipeline
{
    private readonly IReceiptAnalysisWorkflow _workflow;
    private readonly IStructuredVisionProviderResolver _providerResolver;
    private readonly ILogger<AnthropicReceiptExtractor> _logger;

    public AnthropicReceiptExtractor(
        IReceiptAnalysisWorkflow workflow,
        IStructuredVisionProviderResolver providerResolver,
        ILogger<AnthropicReceiptExtractor> logger)
    {
        _workflow = workflow;
        _providerResolver = providerResolver;
        _logger = logger;
    }

    public string Id => AnthropicStructuredVisionProvider.ProviderId;

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
                "Claude returned an invalid receipt structure. Retry or choose another provider.",
                exception);
        }
    }

    public async Task<ReceiptRecognitionStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var status = await _providerResolver.Resolve(Id).GetStatusAsync(cancellationToken);
        return new ReceiptRecognitionStatus(
            status.Available,
            "Claude structured receipt workflow",
            status.Models,
            status.Message);
    }

    [LoggerMessage(
        EventId = 2231,
        Level = LogLevel.Information,
        Message = "Anthropic receipt workflow completed with model {Model} in {DurationMs} ms; {ItemCount} items; correlation {CorrelationId}")]
    private partial void AnalysisCompleted(
        string model,
        long durationMs,
        int itemCount,
        string correlationId);

    [LoggerMessage(
        EventId = 2232,
        Level = LogLevel.Warning,
        Message = "Anthropic receipt workflow rejected provider output after repair; {ValidationErrorCount} validation errors")]
    private partial void InvalidStructuredOutput(int validationErrorCount);
}
