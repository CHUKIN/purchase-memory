using PurchaseMemory.Domain.Receipts;

namespace PurchaseMemory.Application.Receipts.TextLocation;

/// <summary>
/// Keeps each line's printed-text outlines in step with the receipt's lines.
/// Matching is local and deterministic, so it simply runs again whenever the
/// lines are replaced (review, purchase correction): a renamed, moved or
/// deleted line can never keep the outline of another line.
/// </summary>
public interface IReceiptTextHighlighter
{
    /// <summary>Re-matches the receipt's current lines when a text layout is stored; otherwise leaves them alone.</summary>
    Task RefreshAsync(ReceiptCapture receipt, CancellationToken cancellationToken);
}

public sealed class ReceiptTextHighlighter(IReceiptTextLayoutRepository layouts) : IReceiptTextHighlighter
{
    public async Task RefreshAsync(ReceiptCapture receipt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        // Decided by the stored layout, not by the receipt's status: the receipt
        // may have been loaded before the worker committed "Located".
        var layout = await layouts.FindAsync(receipt.Id, cancellationToken);
        if (layout is not null)
        {
            Apply(receipt, layout);
        }
    }

    public static void Apply(ReceiptCapture receipt, ReceiptTextLayout layout)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(layout);
        var items = receipt.Items.OrderBy(item => item.Position).ToList();
        var found = ReceiptTextMatcher.Locate(
            layout,
            items.Select(item => new ReceiptTextMatchItem(item.Name, item.LineTotal)).ToList());
        receipt.ApplyTextHighlights(found.ToDictionary(pair => items[pair.Key].Position, pair => pair.Value));
    }
}
