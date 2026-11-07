using Microsoft.EntityFrameworkCore;
using PurchaseMemory.Application.Products.PhotoSearch;
using PurchaseMemory.Domain.Purchases;

namespace PurchaseMemory.Infrastructure.Persistence;

/// <summary>
/// The candidate list for «Найти по фото» (provider-neutral EF Core). A
/// household catalog is hundreds of products and a few thousand linked lines,
/// so three flat reads ranked in memory are simpler than a correlated SQL
/// ranking and behave the same on SQLite and Postgres.
/// </summary>
public sealed class ProductPhotoCandidateRepository(PurchaseMemoryDbContext dbContext) : IProductPhotoCandidateRepository
{
    public async Task<IReadOnlyList<ProductPhotoCandidateRow>> ListCandidatesAsync(
        int maximumCandidates,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCandidates);

        var products = await dbContext.Products
            .AsNoTracking()
            .Select(product => new
            {
                product.Id,
                product.CanonicalName,
                product.NamePl,
                product.NameRu,
                product.Brand,
                product.CategoryId,
                product.UpdatedAt
            })
            .ToListAsync(cancellationToken);
        if (products.Count == 0)
        {
            return [];
        }

        // One row per (product, purchase); a purchase without a receipt date
        // counts at the moment it was saved, as in the purchase feed.
        // A join on the shadow key, not SelectMany over the navigation: SQLite has no APPLY.
        var purchases = await dbContext.Set<PurchaseItem>()
            .AsNoTracking()
            .Where(item => item.ProductId != null)
            .Join(
                dbContext.PurchaseDrafts.AsNoTracking(),
                item => EF.Property<Guid>(item, "PurchaseDraftId"),
                draft => draft.Id,
                (item, draft) => new { ProductId = item.ProductId!.Value, DraftId = draft.Id, At = draft.PurchasedAt ?? draft.CreatedAt })
            .Distinct()
            .ToListAsync(cancellationToken);
        var history = purchases
            .GroupBy(row => row.ProductId)
            .ToDictionary(
                group => group.Key,
                group => (Last: group.Max(row => row.At), Count: group.Select(row => row.DraftId).Distinct().Count()));

        var categories = await dbContext.ProductCategories
            .AsNoTracking()
            .Select(category => new { category.Id, category.Name })
            .ToDictionaryAsync(category => category.Id, category => category.Name, cancellationToken);

        return products
            .Select(product =>
            {
                var bought = history.TryGetValue(product.Id, out var facts);
                var row = new ProductPhotoCandidateRow(
                    new ProductPhotoCandidate(
                        product.Id,
                        product.CanonicalName,
                        product.NamePl,
                        product.NameRu,
                        product.Brand,
                        product.CategoryId is { } categoryId && categories.TryGetValue(categoryId, out var name) ? name : null),
                    bought ? facts.Last : null,
                    bought ? facts.Count : 0);
                return (Row: row, product.UpdatedAt);
            })
            .OrderByDescending(entry => entry.Row.LastPurchasedAt.HasValue)
            .ThenByDescending(entry => entry.Row.LastPurchasedAt)
            .ThenByDescending(entry => entry.Row.PurchaseCount)
            .ThenByDescending(entry => entry.UpdatedAt)
            .ThenBy(entry => entry.Row.Candidate.ProductId)
            .Take(maximumCandidates)
            .Select(entry => entry.Row)
            .ToList();
    }
}
