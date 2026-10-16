using Microsoft.EntityFrameworkCore;
using PurchaseMemory.Application.Purchases;
using PurchaseMemory.Application.ReviewQueue;
using PurchaseMemory.Domain.Products;
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

        var flaggedProducts = await dbContext.Products
            .AsNoTracking()
            .Where(product => product.NeedsReview)
            .Select(product => new
            {
                product.Id,
                product.CanonicalName,
                product.CreatedAt,
                PurchaseCount = dbContext.PurchaseDrafts
                    .Count(draft => draft.Items.Any(item => item.ProductId == product.Id))
            })
            .ToListAsync(cancellationToken);
        var candidates = await LoadCandidatesAsync(flaggedProducts.Select(product => product.Id).ToList(), cancellationToken);
        var flaggedFacts = flaggedProducts
            .Select(product => new ReviewFlaggedProductFact(
                product.Id,
                product.CanonicalName,
                product.CreatedAt,
                product.PurchaseCount,
                candidates.GetValueOrDefault(product.Id, [])))
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

        return new ReviewQueueFacts(receipts, purchases, flaggedFacts, products);
    }

    /// <summary>
    /// What each flagged product might really be, best first, with what a
    /// person needs to recognise it. Flat queries instead of a nested
    /// projection keep the SQL identical on SQLite and Postgres; candidates
    /// whose product is gone are skipped.
    /// </summary>
    private async Task<Dictionary<Guid, IReadOnlyList<ReviewQueueCandidate>>> LoadCandidatesAsync(
        List<Guid> productIds,
        CancellationToken cancellationToken)
    {
        if (productIds.Count == 0)
        {
            return [];
        }

        var rows = await dbContext.ProductReviewCandidates
            .AsNoTracking()
            .Where(candidate => productIds.Contains(candidate.ProductId))
            .Select(candidate => new { candidate.ProductId, candidate.CandidateProductId, candidate.Score, candidate.Position })
            .ToListAsync(cancellationToken);

        var candidateIds = rows.Select(row => row.CandidateProductId).Distinct().ToList();
        var products = await dbContext.Products
            .AsNoTracking()
            .Where(product => candidateIds.Contains(product.Id))
            .Select(product => new
            {
                product.Id,
                product.CanonicalName,
                product.Brand,
                product.CategoryId,
                PurchaseCount = dbContext.PurchaseDrafts
                    .Count(draft => draft.Items.Any(item => item.ProductId == product.Id))
            })
            .ToDictionaryAsync(product => product.Id, cancellationToken);

        return rows
            .Where(row => products.ContainsKey(row.CandidateProductId))
            .GroupBy(row => row.ProductId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ReviewQueueCandidate>)group
                    .OrderByDescending(row => row.Score)
                    .ThenBy(row => row.Position)
                    .Select(row =>
                    {
                        var product = products[row.CandidateProductId];
                        return new ReviewQueueCandidate(
                            product.Id, product.CanonicalName, product.Brand, product.CategoryId, product.PurchaseCount, row.Score);
                    })
                    .ToList());
    }
}
