using Microsoft.EntityFrameworkCore;
using PurchaseMemory.Application.Purchases;
using PurchaseMemory.Application.ReviewQueue;
using PurchaseMemory.Domain.Products;
using PurchaseMemory.Domain.Purchases;
using PurchaseMemory.Domain.Receipts;
using PurchaseMemory.Infrastructure.Persistence;

namespace PurchaseMemory.Infrastructure.ReviewQueue;

/// <summary>
/// Provider-neutral reads for the review queue. Money is compared in memory
/// because SQLite cannot sum decimals in SQL; the scan is bounded.
/// </summary>
public sealed class ReviewQueueRepository(PurchaseMemoryDbContext dbContext) : IReviewQueueRepository
{
    public async Task<ReviewQueueFacts> LoadFactsAsync(int maximumPurchases, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPurchases);

        var receipts = await dbContext.ReceiptCaptures
            .AsNoTracking()
            .Where(receipt => receipt.Status == ReceiptProcessingStatus.Failed
                || receipt.Status == ReceiptProcessingStatus.ReadyForReview)
            .Select(receipt => new ReviewReceiptFact(
                receipt.Id,
                receipt.Status,
                receipt.MerchantName,
                receipt.PurchasedAt,
                receipt.UpdatedAt))
            .ToListAsync(cancellationToken);

        var purchases = (await dbContext.PurchaseDrafts
                .AsNoTracking()
                .OrderByDescending(draft => draft.CreatedAt)
                .Take(maximumPurchases)
                .Select(draft => new
                {
                    draft.Id,
                    draft.SourceReceiptId,
                    draft.MerchantName,
                    draft.PurchasedAt,
                    draft.CreatedAt,
                    draft.Currency,
                    draft.ReceiptTotal,
                    Items = draft.Items
                        .Select(item => new { item.Position, item.Name, item.UnitPrice, item.LineTotal, item.Kind, item.ProductId })
                        .ToList()
                })
                .ToListAsync(cancellationToken))
            .Select(draft => new ReviewPurchaseFact(
                draft.Id,
                draft.SourceReceiptId,
                draft.MerchantName,
                draft.PurchasedAt,
                draft.CreatedAt,
                draft.Currency,
                draft.ReceiptTotal,
                draft.Items
                    .OrderBy(item => item.Position)
                    .Select(item => new PurchaseDraftIssueItem(
                        item.Position, item.Name, item.UnitPrice, item.LineTotal, Kind: item.Kind))
                    .ToList(),
                draft.Items
                    .Where(item => item.ProductId != null)
                    .Select(item => item.ProductId!.Value)
                    .ToHashSet()))
            .ToList();

        var pendingMatches = await (
                from match in dbContext.PendingProductMatches.AsNoTracking()
                join item in dbContext.Set<PurchaseItem>() on match.PurchaseItemId equals item.Id
                join draft in dbContext.PurchaseDrafts on EF.Property<Guid>(item, "PurchaseDraftId") equals draft.Id
                select new
                {
                    match.Id,
                    match.RawItemName,
                    match.RaisedAt,
                    PurchaseId = draft.Id,
                    draft.SourceReceiptId,
                    item.Position,
                    BestCandidate = match.Candidates
                        .OrderByDescending(candidate => candidate.Score)
                        .ThenBy(candidate => candidate.Position)
                        .Select(candidate => (Guid?)candidate.ProductId)
                        .FirstOrDefault()
                })
            .ToListAsync(cancellationToken);
        var candidates = await LoadCandidatesAsync(pendingMatches.Select(match => match.Id).ToList(), cancellationToken);
        var pendingMatchFacts = pendingMatches
            .Select(match => new ReviewPendingMatchFact(
                match.Id,
                match.RawItemName,
                match.RaisedAt,
                match.PurchaseId,
                match.SourceReceiptId,
                match.BestCandidate,
                match.Position,
                candidates.GetValueOrDefault(match.Id, [])))
            .ToList();

        var products = await (
                from product in dbContext.Products.AsNoTracking()
                where product.CategorySource == null
                    || product.CategorySource == ProductAttributeSource.Suggested
                join category in dbContext.ProductCategories on product.CategoryId equals category.Id into categories
                from category in categories.DefaultIfEmpty()
                select new ReviewProductFact(
                    product.Id,
                    product.CanonicalName,
                    product.CategoryId,
                    category == null ? null : category.Name,
                    product.CategorySource,
                    product.UpdatedAt))
            .ToListAsync(cancellationToken);

        return new ReviewQueueFacts(receipts, purchases, pendingMatchFacts, products);
    }

    /// <summary>
    /// The products each pending match weighed, best first, with what a person
    /// needs to recognise them. Two flat queries instead of a nested projection
    /// keep the SQL identical on SQLite and Postgres; candidates whose product
    /// is gone are skipped, as in the purchase editor.
    /// </summary>
    private async Task<Dictionary<Guid, IReadOnlyList<ReviewQueueCandidate>>> LoadCandidatesAsync(
        List<Guid> matchIds,
        CancellationToken cancellationToken)
    {
        if (matchIds.Count == 0)
        {
            return [];
        }

        var rows = await dbContext.Set<PendingProductMatchCandidate>()
            .AsNoTracking()
            .Where(candidate => matchIds.Contains(EF.Property<Guid>(candidate, "PendingProductMatchId")))
            .Select(candidate => new
            {
                MatchId = EF.Property<Guid>(candidate, "PendingProductMatchId"),
                candidate.ProductId,
                candidate.Score,
                candidate.Position
            })
            .ToListAsync(cancellationToken);

        var productIds = rows.Select(row => row.ProductId).Distinct().ToList();
        var products = await dbContext.Products
            .AsNoTracking()
            .Where(product => productIds.Contains(product.Id))
            .Select(product => new ReviewQueueCandidate(product.Id, product.CanonicalName, product.Brand, product.CategoryId))
            .ToDictionaryAsync(product => product.ProductId, cancellationToken);

        return rows
            .Where(row => products.ContainsKey(row.ProductId))
            .GroupBy(row => row.MatchId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ReviewQueueCandidate>)group
                    .OrderByDescending(row => row.Score)
                    .ThenBy(row => row.Position)
                    .Select(row => products[row.ProductId])
                    .ToList());
    }
}
