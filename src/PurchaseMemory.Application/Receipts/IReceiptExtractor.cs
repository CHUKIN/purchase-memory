namespace PurchaseMemory.Application.Receipts;

public interface IReceiptExtractor
{
    Task<ReceiptExtractionResult> ExtractAsync(
        IReadOnlyList<ReceiptImageData> images,
        CancellationToken cancellationToken);
}

public sealed record ReceiptImageData(
    ReadOnlyMemory<byte> Content,
    string ContentType);
