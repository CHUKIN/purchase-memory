using PurchaseMemory.Domain.Receipts;

namespace PurchaseMemory.Application.Receipts;

/// <param name="BoundingBox">Best-effort location of the line on the photo, or <c>null</c>.</param>
/// <param name="Kind">
/// Product or discount. Providers return every row as a product; the shared
/// <see cref="ReceiptExtractionPostprocessor"/> decides which rows are discounts.
/// </param>
public sealed record ReceiptExtractionItem(
    string Name,
    decimal? Quantity,
    string? Unit,
    decimal? UnitPrice,
    decimal? LineTotal,
    BoundingBox? BoundingBox = null,
    ReceiptLineKind Kind = ReceiptLineKind.Product);

/// <param name="ReceiptTotalBoundingBox">Best-effort location of the printed total, or <c>null</c>.</param>
public sealed record ReceiptExtractionResult(
    string? MerchantName,
    DateTimeOffset? PurchasedAt,
    string? Currency,
    decimal? ReceiptTotal,
    IReadOnlyList<ReceiptExtractionItem> Items,
    string ExtractorName,
    string? ModelName,
    BoundingBox? ReceiptTotalBoundingBox = null);
