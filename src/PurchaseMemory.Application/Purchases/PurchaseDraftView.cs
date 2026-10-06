using PurchaseMemory.Domain.Receipts;

namespace PurchaseMemory.Application.Purchases;

public sealed record PurchaseItemView(
    string Name,
    decimal? Quantity,
    string? Unit,
    decimal? UnitPrice,
    decimal? LineTotal,
    ReceiptLineKind Kind = ReceiptLineKind.Product,
    int? DiscountedItemPosition = null,
    // Set only when the feed is filtered by category or brand: true for the
    // lines that made the purchase match, so the UI can point at them.
    bool? MatchesFilter = null);

public sealed record PurchaseDraftView(
    Guid PurchaseId,
    string? MerchantName,
    DateTimeOffset? PurchasedAt,
    string? Currency,
    decimal? ReceiptTotal,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<PurchaseItemView> Items,
    IReadOnlyList<PurchaseDraftIssue> Issues,
    Guid? SourceReceiptId);
