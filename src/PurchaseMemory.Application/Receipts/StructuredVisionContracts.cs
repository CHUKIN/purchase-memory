namespace PurchaseMemory.Application.Receipts;

/// <summary>
/// Provider-neutral request produced by an application workflow.
/// Provider adapters must forward these values without changing receipt rules.
/// </summary>
public sealed record StructuredVisionRequest(
    IReadOnlyList<ReceiptImageData> Images,
    string Prompt,
    string JsonSchema,
    string PromptVersion,
    string SchemaVersion,
    string CorrelationId);

/// <summary>
/// Structured payload plus safe provider diagnostics. Raw JSON is never logged.
/// </summary>
public sealed record StructuredVisionResponse(
    string RawJson,
    string Provider,
    string Model,
    string? ProviderRequestId,
    string? FinishReason,
    long? InputTokens,
    long? OutputTokens,
    long DurationMilliseconds);

public sealed record StructuredVisionProviderStatus(
    bool Available,
    string Provider,
    IReadOnlyList<string> Models,
    string? Message);

public interface IStructuredVisionProvider
{
    /// <summary>A stable, case-insensitive configuration identifier.</summary>
    string Id { get; }

    Task<StructuredVisionResponse> GenerateAsync(
        StructuredVisionRequest request,
        CancellationToken cancellationToken);

    Task<StructuredVisionProviderStatus> GetStatusAsync(CancellationToken cancellationToken);
}

public interface IStructuredVisionProviderResolver
{
    IStructuredVisionProvider Resolve(string providerId);
}
