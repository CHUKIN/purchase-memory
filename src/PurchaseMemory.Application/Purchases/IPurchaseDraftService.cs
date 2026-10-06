namespace PurchaseMemory.Application.Purchases;

/// <summary>
/// Application use cases shared by MCP today and other interfaces in future.
/// </summary>
public interface IPurchaseDraftService
{
    Task<PurchaseDraftView> CreateAsync(
        CreatePurchaseDraftCommand command,
        CancellationToken cancellationToken);

    Task<PurchaseDraftView> UpdateAsync(
        UpdatePurchaseDraftCommand command,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PurchaseDraftView>> SearchAsync(
        string? query,
        CancellationToken cancellationToken);

    /// <summary>
    /// Chronological purchase feed with date-range, review-status and
    /// free-text filters combined with AND semantics.
    /// </summary>
    Task<PurchaseFeedPage> GetFeedAsync(
        PurchaseFeedQuery query,
        CancellationToken cancellationToken);

    Task<PurchaseDraftView?> FindBySourceReceiptIdAsync(
        Guid sourceReceiptId,
        CancellationToken cancellationToken);
}
