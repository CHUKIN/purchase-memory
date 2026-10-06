using System.ComponentModel;
using PurchaseMemory.Application.Purchases;

namespace PurchaseMemory.Api.Mcp;

public sealed record UpdatePurchaseDraftRequest
{
    [Description("Identifier returned by create_purchase_draft.")]
    public required Guid PurchaseId { get; init; }

    [Description("Complete corrected merchant name, or null when unknown.")]
    public string? MerchantName { get; init; }

    [Description("Complete corrected purchase date and time in ISO 8601 format, or null when unknown.")]
    public DateTimeOffset? PurchasedAt { get; init; }

    [Description("Complete corrected three-letter ISO 4217 currency code, or null when unknown.")]
    public string? Currency { get; init; }

    [Description("Complete corrected receipt total, or null when unknown.")]
    public decimal? ReceiptTotal { get; init; }

    [Description("Complete corrected item list. Items omitted from this list are removed from the draft.")]
    public required IReadOnlyList<PurchaseItemRequest> Items { get; init; }

    public UpdatePurchaseDraftCommand ToCommand()
    {
        return new UpdatePurchaseDraftCommand(
            PurchaseId,
            MerchantName,
            PurchasedAt,
            Currency,
            ReceiptTotal,
            Items.Select(item => item.ToCommand()).ToList());
    }
}
