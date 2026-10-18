using PurchaseMemory.Domain.Receipts;

namespace PurchaseMemory.Application.Purchases;

/// <summary>
/// A recognition problem that a human has to resolve before the draft can be
/// trusted. Types stay coarse on purpose: every type must map to a specific,
/// actionable review card in the UI.
/// </summary>
public enum PurchaseDraftIssueType
{
    /// <summary>The sum of line totals disagrees with the recognized receipt total.</summary>
    ReceiptTotalMismatch,

    /// <summary>An item has neither a unit price nor a line total recognized.</summary>
    MissingItemPrice
}

/// <summary>
/// One reviewable issue. <see cref="RecognizedValue"/> and
/// <see cref="SuggestedFix"/> are English fallbacks: the frontend renders
/// localized copy from <see cref="Type"/> and the structured fields, and only
/// falls back to these strings for a type it does not know yet.
/// </summary>
/// <param name="Type">What kind of problem this is.</param>
/// <param name="ItemPosition">Zero-based item position, or <c>null</c> for a receipt-level issue.</param>
/// <param name="ItemName">Name of the item the issue is about, or <c>null</c> for a receipt-level issue.</param>
/// <param name="RecognizedValue">What recognition produced, formatted for humans; empty when nothing was recognized.</param>
/// <param name="SuggestedFix">How a reviewer is expected to resolve the issue.</param>
/// <param name="BoundingBox">
/// Where the recognized value sits on the receipt photo, when recognition
/// returned a usable box. <c>null</c> means the UI shows the whole photo.
/// </param>
/// <param name="TextPolygons">
/// The item's printed words on the photo (Azure outlines), when found; the UI
/// then highlights them instead of <paramref name="BoundingBox"/>.
/// </param>
public sealed record PurchaseDraftIssue(
    PurchaseDraftIssueType Type,
    int? ItemPosition,
    string? ItemName,
    string RecognizedValue,
    string SuggestedFix,
    BoundingBox? BoundingBox = null,
    IReadOnlyList<TextPolygon>? TextPolygons = null);

/// <summary>
/// The subset of an item that issue detection needs. Both the saved purchase
/// draft and the not-yet-saved receipt snapshot project into this shape so the
/// two surfaces cannot drift apart.
/// </summary>
public sealed record PurchaseDraftIssueItem(
    int Position,
    string Name,
    decimal? UnitPrice,
    decimal? LineTotal,
    BoundingBox? BoundingBox = null,
    ReceiptLineKind Kind = ReceiptLineKind.Product,
    IReadOnlyList<TextPolygon>? TextPolygons = null);

/// <summary>
/// The single place where "does this draft need attention?" is decided.
/// </summary>
public static class PurchaseDraftIssueDetector
{
    private const decimal MoneyComparisonTolerance = 0.01m;

    public static IReadOnlyList<PurchaseDraftIssue> Detect(
        decimal? receiptTotal,
        string? currency,
        IReadOnlyList<PurchaseDraftIssueItem> items,
        BoundingBox? receiptTotalBoundingBox = null)
    {
        ArgumentNullException.ThrowIfNull(items);

        var issues = new List<PurchaseDraftIssue>();

        // A discount line has no unit price by nature; only its amount matters.
        foreach (var item in items.Where(item =>
            item.LineTotal is null
            || (item.UnitPrice is null && item.Kind == ReceiptLineKind.Product)))
        {
            issues.Add(new PurchaseDraftIssue(
                PurchaseDraftIssueType.MissingItemPrice,
                item.Position,
                item.Name,
                FormatPartialPrice(item, currency),
                "Set the price for this item or remove it.",
                item.BoundingBox,
                item.TextPolygons));
        }

        // A total comparison is only meaningful once every line contributes a
        // number. While line totals are missing the per-item issues above are
        // the actionable ones, and a mismatch card would just repeat them.
        if (receiptTotal is not null && items.Count > 0 && items.All(item => item.LineTotal is not null))
        {
            // Discount lines are negative, so this is the total after discounts.
            var linesTotal = items.Sum(item => item.LineTotal!.Value);
            if (Math.Abs(linesTotal - receiptTotal.Value) > MoneyComparisonTolerance)
            {
                issues.Add(new PurchaseDraftIssue(
                    PurchaseDraftIssueType.ReceiptTotalMismatch,
                    null,
                    null,
                    FormatMoney(linesTotal, currency),
                    "Correct the receipt total or the line totals so that they match.",
                    receiptTotalBoundingBox));
            }
        }

        return issues;
    }

    private static string FormatPartialPrice(PurchaseDraftIssueItem item, string? currency)
    {
        var recognized = item.LineTotal ?? item.UnitPrice;
        return recognized is null ? string.Empty : FormatMoney(recognized.Value, currency);
    }

    private static string FormatMoney(decimal value, string? currency)
    {
        var amount = value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(currency) ? amount : $"{amount} {currency}";
    }
}
