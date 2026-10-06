namespace PurchaseMemory.Domain.Receipts;

/// <summary>
/// What a receipt line stands for. Discounts are kept as their own lines
/// (owner decision, variant A) so the printed prices stay verbatim and the sum
/// of all lines still equals the receipt total.
/// </summary>
public enum ReceiptLineKind
{
    Product = 0,
    Discount = 1
}

/// <summary>The fields a discount link depends on.</summary>
public sealed record ReceiptLineForLinking(ReceiptLineKind Kind, string Name, decimal? LineTotal);

/// <summary>
/// Where a discount applies: to one product line (<see cref="TargetPosition"/>),
/// to the whole receipt, or — neither — to nothing that could be identified.
/// </summary>
public readonly record struct ReceiptDiscountLink(int? TargetPosition, bool AppliesToReceipt);

public static class ReceiptDiscountLinking
{
    // Words that make a discount belong to the whole receipt ("OPUST (podsuma)",
    // "RABAT NA PARAGON", "rabat na całe zakupy"). Compared case-insensitively,
    // with and without Polish diacritics.
    private static readonly string[] ReceiptLevelMarkers =
        ["podsuma", "suma", "na paragon", "na cały", "na caly", "na całe", "na cale", "całość", "calosc"];

    /// <summary>
    /// A receipt prints a product discount directly under the product it
    /// reduces, so such a discount belongs to the nearest preceding product
    /// line. A receipt-level discount — marked as such, or larger than that
    /// product's total — belongs to no product. The link is recomputed from
    /// order every time a line list is built: editing or deleting rows can
    /// never leave a link pointing at the wrong product.
    /// </summary>
    public static ReceiptDiscountLink[] Resolve(IReadOnlyList<ReceiptLineForLinking> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var links = new ReceiptDiscountLink[lines.Count];
        int? lastProduct = null;
        for (var position = 0; position < lines.Count; position++)
        {
            var line = lines[position];
            if (line.Kind != ReceiptLineKind.Discount)
            {
                lastProduct = position;
                continue;
            }

            var receiptLevel = HasReceiptLevelMarker(line.Name)
                || (lastProduct is { } product
                    && line.LineTotal is { } amount
                    && lines[product].LineTotal is { } productTotal
                    && Math.Abs(amount) > productTotal);
            links[position] = receiptLevel
                ? new ReceiptDiscountLink(null, true)
                : new ReceiptDiscountLink(lastProduct, false);
        }

        return links;
    }

    public static bool HasReceiptLevelMarker(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && ReceiptLevelMarkers.Any(marker => name.Contains(marker, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Shared invariants for a line's kind, amount and discount link, used by
    /// both the unsaved receipt line and the saved purchase line.
    /// </summary>
    public static void EnsureValid(
        ReceiptLineKind kind,
        int position,
        decimal? lineTotal,
        int? discountedItemPosition)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), "Unknown receipt line kind.");
        }

        if (kind == ReceiptLineKind.Product)
        {
            if (lineTotal < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(lineTotal),
                    "A product line cannot have a negative total; mark the line as a discount instead.");
            }

            if (discountedItemPosition is not null)
            {
                throw new ArgumentException(
                    "Only a discount line can refer to a discounted item.",
                    nameof(discountedItemPosition));
            }

            return;
        }

        if (lineTotal > 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lineTotal), "A discount line cannot have a positive total; enter the discount as a negative amount or mark the line as a product.");
        }

        if (discountedItemPosition is not null
            && (discountedItemPosition < 0 || discountedItemPosition >= position))
        {
            throw new ArgumentOutOfRangeException(
                nameof(discountedItemPosition),
                "A discount can only refer to a line printed above it.");
        }
    }
}
