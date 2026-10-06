namespace PurchaseMemory.Application.Receipts;

/// <summary>
/// Represents one complete, interchangeable receipt-recognition approach.
/// A pipeline owns every implementation-specific stage needed to turn an image
/// into the application-level <see cref="ReceiptExtractionResult"/> contract.
/// </summary>
public interface IReceiptRecognitionPipeline : IReceiptExtractor, IReceiptRecognitionStatusProvider
{
    /// <summary>
    /// Stable configuration identifier, for example <c>apple</c> or <c>ollama</c>.
    /// </summary>
    string Id { get; }
}

public sealed record ReceiptRecognitionStatus(
    bool Available,
    string Mode,
    IReadOnlyList<string> Models,
    string? Message);

public interface IReceiptRecognitionStatusProvider
{
    Task<ReceiptRecognitionStatus> GetStatusAsync(CancellationToken cancellationToken);
}
