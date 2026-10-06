using System.ComponentModel;
using PurchaseMemory.Application.Purchases;

namespace PurchaseMemory.Api.Mcp;

public sealed record CreatePurchaseDraftRequest
{
    [Description("Merchant or store name exactly as visible on the receipt. Use null when unreadable.")]
    public string? MerchantName { get; init; }

    [Description("Purchase date and time in ISO 8601 format. Use the local offset when known, otherwise null.")]
    public DateTimeOffset? PurchasedAt { get; init; }

    [Description("Three-letter ISO 4217 currency code such as PLN or EUR. Use null when unknown.")]
    public string? Currency { get; init; }

    [Description("Final receipt total as a decimal number without a currency symbol. Use null when unreadable.")]
    public decimal? ReceiptTotal { get; init; }

    [Description("All visible purchase lines in their original receipt order. Never add products that are not visible.")]
    public required IReadOnlyList<PurchaseItemRequest> Items { get; init; }

    [Description(
        "Leave false normally. Set true only after the server reports a matching purchase and " +
        "the user explicitly confirms this is a separate, genuinely duplicated transaction.")]
    public bool AllowPossibleDuplicate { get; init; }

    public CreatePurchaseDraftCommand ToCommand()
    {
        return new CreatePurchaseDraftCommand(
            MerchantName,
            PurchasedAt,
            Currency,
            ReceiptTotal,
            Items.Select(item => item.ToCommand()).ToList(),
            AllowPossibleDuplicate);
    }
}
