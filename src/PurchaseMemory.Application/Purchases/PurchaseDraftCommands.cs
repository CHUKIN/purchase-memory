using PurchaseMemory.Domain.Receipts;

namespace PurchaseMemory.Application.Purchases;

/// <param name="Kind">
/// Product or discount; null lets the backend decide from the amount. A
/// discount's link to its product always follows from line order.
/// </param>
public sealed record PurchaseItemCommand(
    string Name,
    decimal? Quantity,
    string? Unit,
    decimal? UnitPrice,
    decimal? LineTotal,
    ReceiptLineKind? Kind = null);

public sealed record CreatePurchaseDraftCommand(
    string? MerchantName,
    DateTimeOffset? PurchasedAt,
    string? Currency,
    decimal? ReceiptTotal,
    IReadOnlyList<PurchaseItemCommand> Items,
    bool AllowPossibleDuplicate = false,
    Guid? SourceReceiptId = null);

public sealed record UpdatePurchaseDraftCommand(
    Guid PurchaseId,
    string? MerchantName,
    DateTimeOffset? PurchasedAt,
    string? Currency,
    decimal? ReceiptTotal,
    IReadOnlyList<PurchaseItemCommand> Items);
