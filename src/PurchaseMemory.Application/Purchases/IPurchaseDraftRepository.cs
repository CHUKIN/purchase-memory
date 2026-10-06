using PurchaseMemory.Domain.Purchases;

namespace PurchaseMemory.Application.Purchases;

/// <summary>
/// Persistence boundary owned by the application layer. Implementations may use
/// SQLite now and PostgreSQL later without changing the use cases.
/// </summary>
public interface IPurchaseDraftRepository
{
    void Add(PurchaseDraft draft);

    Task<PurchaseDraft?> FindByIdAsync(Guid purchaseId, CancellationToken cancellationToken);

    Task<PurchaseDraft?> FindBySourceReceiptIdAsync(
        Guid sourceReceiptId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PurchaseDraft>> FindPossibleDuplicateCandidatesAsync(
        DateTimeOffset? purchasedAt,
        decimal? receiptTotal,
        int maximumResults,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PurchaseDraft>> SearchAsync(
        string? query,
        int maximumResults,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns feed candidates ordered by the effective purchase moment
    /// (<c>PurchasedAt</c>, falling back to <c>CreatedAt</c>) descending.
    /// The optional bounds are applied to <c>PurchasedAt</c> only, so purchases
    /// without a recognized date are excluded as soon as either bound is set.
    /// At most <paramref name="maximumCandidates"/> rows are returned; free-text
    /// and review-status filtering happen above this boundary.
    /// </summary>
    Task<IReadOnlyList<PurchaseDraft>> ListFeedCandidatesAsync(
        DateTimeOffset? fromInclusive,
        DateTimeOffset? toInclusive,
        int maximumCandidates,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
