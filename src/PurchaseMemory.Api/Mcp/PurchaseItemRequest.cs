using System.ComponentModel;
using PurchaseMemory.Application.Purchases;

namespace PurchaseMemory.Api.Mcp;

public sealed record PurchaseItemRequest
{
    [Description("Original item name exactly as visible on the receipt. Do not translate or invent missing text.")]
    public required string Name { get; init; }

    [Description("Purchased quantity when visible. Use null when the receipt does not show it.")]
    public decimal? Quantity { get; init; }

    [Description("Short unit from the receipt, for example pcs, kg, g, l, or ml. Use null when unknown.")]
    public string? Unit { get; init; }

    [Description("Price for one unit when visible. Use a decimal number without a currency symbol, or null.")]
    public decimal? UnitPrice { get; init; }

    [Description("Total amount for this receipt line. Use a decimal number without a currency symbol, or null. A discount row (OPUST, RABAT) is its own line right after its product, with a negative total.")]
    public decimal? LineTotal { get; init; }

    public PurchaseItemCommand ToCommand()
    {
        return new PurchaseItemCommand(Name, Quantity, Unit, UnitPrice, LineTotal);
    }
}
