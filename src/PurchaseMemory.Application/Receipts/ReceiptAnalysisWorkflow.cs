namespace PurchaseMemory.Application.Receipts;

public sealed class ReceiptAnalysisWorkflow : IReceiptAnalysisWorkflow
{
    private const int MaximumRepairAttempts = 1;

    private readonly IStructuredVisionProviderResolver _providerResolver;

    public ReceiptAnalysisWorkflow(IStructuredVisionProviderResolver providerResolver)
    {
        _providerResolver = providerResolver;
    }

    public async Task<ReceiptAnalysisResult> AnalyzeAsync(
        ReceiptAnalysisRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ReceiptId == Guid.Empty)
        {
            throw new ArgumentException("Receipt identifier cannot be empty.", nameof(request));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(request.CorrelationId);
        ArgumentNullException.ThrowIfNull(request.Images);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ProviderId);
        if (!string.Equals(request.Intent, ReceiptAnalysisContract.Intent, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Only intent '{ReceiptAnalysisContract.Intent}' is supported.",
                nameof(request));
        }

        if (request.Images.Count == 0 || request.Images.Any(image => image.Content.IsEmpty))
        {
            throw new ArgumentException("Receipt images cannot be empty.", nameof(request));
        }


        if (request.Images.Any(image => string.IsNullOrWhiteSpace(image.ContentType)))
        {
            throw new ArgumentException("Every receipt image must have a content type.", nameof(request));
        }

        var instruction = ReceiptAnalysisContract.NormalizeInstruction(request.UserInstruction);
        var provider = _providerResolver.Resolve(request.ProviderId);
        ReceiptStructuredOutputException? lastValidationError = null;

        for (var attempt = 0; attempt <= MaximumRepairAttempts; attempt++)
        {
            var prompt = ReceiptAnalysisContract.BuildPrompt(
                instruction,
                attempt == 0 ? null : lastValidationError!.ValidationErrors);
            var providerResponse = await provider.GenerateAsync(
                new StructuredVisionRequest(
                    request.Images,
                    prompt,
                    ReceiptAnalysisContract.JsonSchema,
                    ReceiptAnalysisContract.PromptVersion,
                    ReceiptAnalysisContract.SchemaVersion,
                    request.CorrelationId),
                cancellationToken);

            try
            {
                var parsed = ReceiptStructuredOutputParser.Parse(providerResponse.RawJson);
                var extraction = parsed.Extraction with
                {
                    ExtractorName = providerResponse.Provider,
                    ModelName = providerResponse.Model
                };
                return new ReceiptAnalysisResult(
                    extraction,
                    parsed.Warnings,
                    new ReceiptAnalysisMetadata(
                        providerResponse.Provider,
                        providerResponse.Model,
                        providerResponse.ProviderRequestId,
                        providerResponse.FinishReason,
                        providerResponse.InputTokens,
                        providerResponse.OutputTokens,
                        providerResponse.DurationMilliseconds,
                        ReceiptAnalysisContract.PromptVersion,
                        ReceiptAnalysisContract.SchemaVersion,
                        request.CorrelationId));
            }
            catch (ReceiptStructuredOutputException exception) when (attempt < MaximumRepairAttempts)
            {
                lastValidationError = exception;
            }
        }

        if (lastValidationError is not null)
        {
            throw lastValidationError;
        }

        throw new InvalidOperationException("Receipt analysis ended without a result.");
    }
}
