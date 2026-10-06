using PurchaseMemory.Domain.Receipts;

namespace PurchaseMemory.Application.Receipts;

public sealed record ReceiptImageUpload(
    Stream Image,
    string DeclaredContentType);

/// <param name="Actor">The signed-in uploader; null only for internal callers.</param>
public sealed record UploadReceiptCommand(
    IReadOnlyList<ReceiptImageUpload> Images,
    ReceiptActor? Actor = null);

/// <param name="BoundingBox">
/// Echoed back by the review UI so a best-effort photo region survives editing.
/// It is validated like any other client input and dropped when unusable.
/// </param>
/// <param name="Kind">
/// Product or discount as the client sees it; null lets the backend decide from
/// the amount (see <see cref="ReceiptLineKindResolver"/>). The discount's link
/// to its product is never taken from the client: it follows from line order.
/// </param>
public sealed record ReceiptItemCommand(
    string Name,
    decimal? Quantity,
    string? Unit,
    decimal? UnitPrice,
    decimal? LineTotal,
    BoundingBox? BoundingBox = null,
    ReceiptLineKind? Kind = null);

public static class ReceiptLineKindResolver
{
    /// <summary>
    /// An explicit kind wins; otherwise a negative amount means a discount,
    /// because a purchased product never has a negative line total. This keeps
    /// older clients (and the MCP tool) that do not send a kind working.
    /// </summary>
    public static ReceiptLineKind Resolve(ReceiptLineKind? kind, decimal? lineTotal) =>
        kind ?? (lineTotal < 0 ? ReceiptLineKind.Discount : ReceiptLineKind.Product);
}

public sealed record ReviewReceiptCommand(
    Guid ReceiptId,
    string? MerchantName,
    DateTimeOffset? PurchasedAt,
    string? Currency,
    decimal? ReceiptTotal,
    IReadOnlyList<ReceiptItemCommand> Items,
    BoundingBox? ReceiptTotalBoundingBox = null,
    ReceiptActor? Actor = null);

public sealed record SaveReceiptCommand(
    Guid ReceiptId,
    bool AllowPossibleDuplicate = false);
