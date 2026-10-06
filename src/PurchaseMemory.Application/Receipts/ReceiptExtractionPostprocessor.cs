using PurchaseMemory.Domain.Receipts;
using System.Text.RegularExpressions;

namespace PurchaseMemory.Application.Receipts;

/// <summary>
/// Code-level cleanup applied to every extraction result regardless of which
/// recognition pipeline produced it (Apple, Ollama, or OpenAI). Prompting a
/// vision LLM to canonicalize merchant names or to label discount rows is
/// unreliable in practice (see РЕЗУЛЬТАТЫ_ТРЕК_B_СРАВНЕНИЕ_МОДЕЛЕЙ.md), so both
/// are decided here. Discount rows are kept as their own lines (owner decision,
/// variant A): the printed product prices stay verbatim and the lines still sum
/// to the receipt total. Sum-vs-total mismatch flagging is intentionally NOT
/// duplicated here: it is computed from the stored lines in
/// <see cref="ReceiptProcessingService"/> and <c>PurchaseDraftIssueDetector</c>.
/// </summary>
public static class ReceiptExtractionPostprocessor
{
    public static ReceiptExtractionResult Apply(ReceiptExtractionResult extraction)
    {
        ArgumentNullException.ThrowIfNull(extraction);

        var items = ReceiptDiscountLineFilter.ClassifyDiscountLines(extraction.Items);
        var merchantName = ReceiptMerchantCanonicalizer.Canonicalize(extraction.MerchantName);

        return extraction with
        {
            MerchantName = merchantName,
            Items = items
        };
    }
}

/// <summary>
/// Marks discount/promo rows (e.g. "OPUST -2,50", "RABAT KARTA STALEGO
/// KLIENTA") as discount lines instead of products. Matching is intentionally
/// simple: a negative total, or a case-insensitive substring check against a
/// small set of known Polish (and generic) discount markers.
/// </summary>
public static class ReceiptDiscountLineFilter
{
    private static readonly string[] DiscountMarkers =
    [
        "OPUST",
        "RABAT",
        "UPUST",
        "PROMOCJA",
        "ZNIZKA",
        "ZNIŻKA",
        "DISCOUNT",
        "SKONTO"
    ];

    /// <summary>
    /// Returns the same rows in the same order with discount rows marked as
    /// <see cref="ReceiptLineKind.Discount"/>. A discount amount the model
    /// returned as a positive number is stored negative, so every line keeps
    /// adding up to the receipt total.
    /// </summary>
    public static IReadOnlyList<ReceiptExtractionItem> ClassifyDiscountLines(
        IReadOnlyList<ReceiptExtractionItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return items
            .Select(item => IsDiscountLine(item)
                ? item with
                {
                    Kind = ReceiptLineKind.Discount,
                    LineTotal = item.LineTotal is > 0 ? -item.LineTotal : item.LineTotal
                }
                : item)
            .ToList();
    }

    /// <summary>
    /// A row is a discount when its total is negative, or when its name carries
    /// a known marker and it has no unit price. The first rule keeps
    /// OCR-garbled discount rows (e.g. "OPUS A ES A ANE-" for "OPUST") out of
    /// the product list: a purchased product never has a negative line total,
    /// and the parser flags such unmarked rows with a review warning. The unit
    /// price condition keeps a real product whose name merely contains a
    /// marker ("… PROMOCJA 1 x 3,99") a product.
    /// </summary>
    public static bool IsDiscountLine(ReceiptExtractionItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Kind == ReceiptLineKind.Discount
            || item.LineTotal < 0
            || (item.UnitPrice is null && IsDiscountLine(item.Name));
    }

    public static bool IsDiscountLine(string? itemName)
    {
        if (string.IsNullOrWhiteSpace(itemName))
        {
            return false;
        }

        var normalized = itemName.Trim();
        return DiscountMarkers.Any(marker =>
            normalized.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Canonicalizes merchant name variants (e.g. OCR/LLM noise, legal-entity
/// suffixes, store-number suffixes) to a single brand name, seeded with the
/// retail chains that appeared in the Track B receipt sample. This is a
/// simple static lookup, not a general NLP/fuzzy-matching system: unknown
/// merchant names are returned unchanged (trimmed).
/// </summary>
public static class ReceiptMerchantCanonicalizer
{
    private static readonly (string Canonical, string[] Variants)[] Brands =
    [
        ("Biedronka", ["biedronka", "jeronimo martins polska"]),
        ("Lidl", ["lidl"]),
        ("Auchan", ["auchan"]),
        ("Carrefour", ["carrefour"]),
        ("Kaufland", ["kaufland"]),
        ("Żabka", ["zabka", "żabka"]),
        ("Netto", ["netto"]),
        ("Dino", ["dino polska", "dino"]),
        ("Aldi", ["aldi"]),
        ("Tesco", ["tesco"]),
        ("Rossmann", ["rossmann"]),
        ("Stokrotka", ["stokrotka"]),
        ("Intermarché", ["intermarche", "intermarché"]),
    ];

    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    public static string? Canonicalize(string? merchantName)
    {
        if (string.IsNullOrWhiteSpace(merchantName))
        {
            return merchantName;
        }

        var trimmed = merchantName.Trim();
        var normalized = Normalize(trimmed);

        foreach (var brand in Brands)
        {
            if (brand.Variants.Any(variant => normalized.Contains(Normalize(variant))))
            {
                return brand.Canonical;
            }
        }

        return trimmed;
    }

    private static string Normalize(string value) =>
        WhitespaceRegex.Replace(value, " ").Trim().ToLowerInvariant();
}
