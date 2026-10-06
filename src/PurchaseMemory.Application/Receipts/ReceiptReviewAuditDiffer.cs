using System.Globalization;
using PurchaseMemory.Domain.Receipts;

namespace PurchaseMemory.Application.Receipts;

/// <summary>
/// Computes the human-meaningful differences between a receipt's current
/// recognized/reviewed state and an incoming review submission, so each real
/// edit can be recorded as an audit entry. Bounding-box-only changes are
/// recognition artifacts, not human edits, and are intentionally excluded.
/// </summary>
public static class ReceiptReviewAuditDiffer
{
    public static IReadOnlyList<ReceiptReviewAuditEntry> Diff(
        ReceiptCapture receipt,
        string? newMerchantName,
        DateTimeOffset? newPurchasedAt,
        string? newCurrency,
        decimal? newReceiptTotal,
        IReadOnlyList<ReceiptCaptureItem> newItems,
        ReceiptActor actor,
        DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(newItems);

        var entries = new List<ReceiptReviewAuditEntry>();

        AddIfChanged(entries, actor, occurredAt, "MerchantName", receipt.MerchantName, Trimmed(newMerchantName));
        AddIfChanged(entries, actor, occurredAt, "PurchasedAt", receipt.PurchasedAt, newPurchasedAt);
        AddIfChanged(entries, actor, occurredAt, "Currency", receipt.Currency, Trimmed(newCurrency)?.ToUpperInvariant());
        AddIfChanged(entries, actor, occurredAt, "ReceiptTotal", receipt.ReceiptTotal, newReceiptTotal);

        var oldItems = receipt.Items.OrderBy(item => item.Position).ToList();
        var orderedNewItems = newItems.OrderBy(item => item.Position).ToList();
        var commonCount = Math.Min(oldItems.Count, orderedNewItems.Count);

        for (var index = 0; index < commonCount; index++)
        {
            var oldItem = oldItems[index];
            var newItem = orderedNewItems[index];
            var fieldPrefix = $"Item:{index}";

            AddIfChanged(entries, actor, occurredAt, $"{fieldPrefix}.Name", oldItem.Name, Trimmed(newItem.Name));
            AddIfChanged(entries, actor, occurredAt, $"{fieldPrefix}.Quantity", oldItem.Quantity, newItem.Quantity);
            AddIfChanged(entries, actor, occurredAt, $"{fieldPrefix}.Unit", oldItem.Unit, Trimmed(newItem.Unit));
            AddIfChanged(entries, actor, occurredAt, $"{fieldPrefix}.UnitPrice", oldItem.UnitPrice, newItem.UnitPrice);
            AddIfChanged(entries, actor, occurredAt, $"{fieldPrefix}.LineTotal", oldItem.LineTotal, newItem.LineTotal);
            AddIfChanged(entries, actor, occurredAt, $"{fieldPrefix}.Kind", oldItem.Kind.ToString(), newItem.Kind.ToString());
        }

        for (var index = commonCount; index < oldItems.Count; index++)
        {
            var removed = oldItems[index];
            entries.Add(ReceiptReviewAuditEntry.Create(
                actor.UserId,
                actor.Email,
                $"Item:{index}",
                ReceiptReviewAuditChangeType.ItemRemoved,
                DescribeItem(removed),
                null,
                occurredAt));
        }

        for (var index = commonCount; index < orderedNewItems.Count; index++)
        {
            var added = orderedNewItems[index];
            entries.Add(ReceiptReviewAuditEntry.Create(
                actor.UserId,
                actor.Email,
                $"Item:{index}",
                ReceiptReviewAuditChangeType.ItemAdded,
                null,
                DescribeItem(added),
                occurredAt));
        }

        return entries;
    }

    private static void AddIfChanged(
        List<ReceiptReviewAuditEntry> entries,
        ReceiptActor actor,
        DateTimeOffset occurredAt,
        string fieldName,
        string? oldValue,
        string? newValue)
    {
        if (AreEquivalent(oldValue, newValue))
        {
            return;
        }

        entries.Add(ReceiptReviewAuditEntry.Create(
            actor.UserId,
            actor.Email,
            fieldName,
            ReceiptReviewAuditChangeType.Modified,
            oldValue,
            newValue,
            occurredAt));
    }

    /// <summary>
    /// Compares by value, not by formatted string: a decimal round-tripped
    /// through the database can gain trailing zeros (e.g. "1" becomes "1.0"),
    /// which must never read as a human edit.
    /// </summary>
    private static void AddIfChanged(
        List<ReceiptReviewAuditEntry> entries,
        ReceiptActor actor,
        DateTimeOffset occurredAt,
        string fieldName,
        decimal? oldValue,
        decimal? newValue)
    {
        if (oldValue == newValue)
        {
            return;
        }

        AddIfChanged(entries, actor, occurredAt, fieldName, Format(oldValue), Format(newValue));
    }

    private static void AddIfChanged(
        List<ReceiptReviewAuditEntry> entries,
        ReceiptActor actor,
        DateTimeOffset occurredAt,
        string fieldName,
        DateTimeOffset? oldValue,
        DateTimeOffset? newValue)
    {
        if (oldValue == newValue)
        {
            return;
        }

        AddIfChanged(entries, actor, occurredAt, fieldName, Format(oldValue), Format(newValue));
    }

    private static bool AreEquivalent(string? left, string? right)
    {
        var normalizedLeft = string.IsNullOrWhiteSpace(left) ? null : left;
        var normalizedRight = string.IsNullOrWhiteSpace(right) ? null : right;
        return string.Equals(normalizedLeft, normalizedRight, StringComparison.Ordinal);
    }

    private static string DescribeItem(ReceiptCaptureItem item)
    {
        var quantity = Format(item.Quantity);
        var lineTotal = Format(item.LineTotal);
        var kind = item.Kind == ReceiptLineKind.Discount ? "discount, " : string.Empty;
        return $"{item.Name} ({kind}qty={quantity ?? "-"}, total={lineTotal ?? "-"})";
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Format(decimal? value) =>
        value?.ToString(CultureInfo.InvariantCulture);

    private static string? Format(DateTimeOffset? value) =>
        value?.ToString("O", CultureInfo.InvariantCulture);
}
