using Microsoft.EntityFrameworkCore;
using PurchaseMemory.Application.Analytics;
using PurchaseMemory.Application.Products;
using PurchaseMemory.Infrastructure.Persistence;

namespace PurchaseMemory.Infrastructure.Analytics;

/// <summary>Provider-neutral EF Core reads for analytics; both SQLite and Postgres run the same queries.</summary>
public sealed class AnalyticsRepository : IAnalyticsRepository
{
    /// <summary>Only the most frequent stores and brands are offered to the model; the rest still validate.</summary>
    public const int MaximumMerchants = 100;
    public const int MaximumBrands = 200;

    private readonly PurchaseMemoryDbContext _dbContext;

    public AnalyticsRepository(PurchaseMemoryDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<AnalyticsVocabulary> GetVocabularyAsync(CancellationToken cancellationToken)
    {
        var categories = await _dbContext.ProductCategories
            .AsNoTracking()
            .Select(category => new { category.Id, category.ParentId, category.Name, category.SystemKey, category.Position })
            .ToListAsync(cancellationToken);
        var ordered = categories
            .Where(category => category.ParentId is null)
            .OrderBy(category => category.Position)
            .SelectMany(group => new[] { group }.Concat(categories
                .Where(child => child.ParentId == group.Id)
                .OrderBy(child => child.Position)))
            .Select(category => new AnalyticsCategory(category.Id, category.ParentId, category.Name, category.SystemKey))
            .ToList();

        var merchantNames = await _dbContext.PurchaseDrafts
            .AsNoTracking()
            .Where(draft => draft.MerchantName != null)
            .Select(draft => draft.MerchantName!)
            .ToListAsync(cancellationToken);
        var brandNames = await _dbContext.Products
            .AsNoTracking()
            .Where(product => product.Brand != null)
            .Select(product => product.Brand!)
            .ToListAsync(cancellationToken);

        // Every tag, so the validator knows them all; most used first (then alphabetically),
        // because the planner offers the model only the head of the list. Sorted in .NET so
        // SQLite and Postgres agree.
        var tagUse = await _dbContext.ProductTagLinks
            .AsNoTracking()
            .GroupBy(link => link.TagId)
            .Select(group => new { TagId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(entry => entry.TagId, entry => entry.Count, cancellationToken);
        var tags = (await _dbContext.ProductTags
                .AsNoTracking()
                .Select(tag => new { tag.Id, tag.Name, tag.NormalizedName })
                .ToListAsync(cancellationToken))
            .OrderByDescending(tag => tagUse.GetValueOrDefault(tag.Id))
            .ThenBy(tag => tag.NormalizedName, StringComparer.Ordinal)
            .ThenBy(tag => tag.Id)
            .Select(tag => new AnalyticsTag(tag.Id, tag.Name))
            .ToList();

        return new AnalyticsVocabulary(
            ordered,
            MostFrequent(merchantNames, MaximumMerchants),
            MostFrequent(brandNames, MaximumBrands),
            tags);
    }

    public async Task<IReadOnlySet<Guid>> FindExistingProductIdsAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(productIds);
        if (productIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var identifiers = productIds.Distinct().ToList();
        return (await _dbContext.Products
                .AsNoTracking()
                .Where(product => identifiers.Contains(product.Id))
                .Select(product => product.Id)
                .ToListAsync(cancellationToken))
            .ToHashSet();
    }

    public async Task<IReadOnlyList<AnalyticsPurchaseFact>> LoadPurchasesAsync(
        DateTimeOffset? fromInclusive,
        DateTimeOffset? toExclusive,
        int maximumPurchases,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPurchases);

        var drafts = _dbContext.PurchaseDrafts.AsNoTracking();
        if (fromInclusive is { } from)
        {
            drafts = drafts.Where(draft => (draft.PurchasedAt ?? draft.CreatedAt) >= from);
        }

        if (toExclusive is { } to)
        {
            drafts = drafts.Where(draft => (draft.PurchasedAt ?? draft.CreatedAt) < to);
        }

        var purchases = await drafts
            .OrderByDescending(draft => draft.PurchasedAt ?? draft.CreatedAt)
            .ThenBy(draft => draft.Id)
            .Take(maximumPurchases)
            .Include(draft => draft.Items)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        var productIds = purchases
            .SelectMany(purchase => purchase.Items)
            .Where(item => item.ProductId is not null)
            .Select(item => item.ProductId!.Value)
            .Distinct()
            .ToList();
        var products = productIds.Count == 0
            ? []
            : await _dbContext.Products
                .AsNoTracking()
                .Where(product => productIds.Contains(product.Id))
                .Select(product => new { product.Id, product.CanonicalName, product.NormalizedName, product.CategoryId, product.Brand })
                .ToListAsync(cancellationToken);
        var aliases = productIds.Count == 0
            ? []
            : await _dbContext.ProductAliases
                .AsNoTracking()
                .Where(alias => productIds.Contains(alias.ProductId))
                .Select(alias => new { alias.ProductId, alias.NormalizedText })
                .ToListAsync(cancellationToken);
        var tagLinks = productIds.Count == 0
            ? []
            : await _dbContext.ProductTagLinks
                .AsNoTracking()
                .Where(link => productIds.Contains(link.ProductId))
                .Select(link => new { link.ProductId, link.TagId })
                .ToListAsync(cancellationToken);
        var tagsByProduct = tagLinks
            .GroupBy(link => link.ProductId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<Guid>)group.Select(link => link.TagId).Order().ToList());
        var aliasText = aliases
            .GroupBy(alias => alias.ProductId)
            .ToDictionary(group => group.Key, group => string.Join(' ', group.Select(alias => alias.NormalizedText)));
        var byId = products.ToDictionary(product => product.Id);

        return purchases
            .Select(purchase => new AnalyticsPurchaseFact(
                purchase.Id,
                purchase.SourceReceiptId,
                purchase.PurchasedAt ?? purchase.CreatedAt,
                purchase.PurchasedAt is not null,
                purchase.MerchantName,
                purchase.Currency,
                purchase.Items
                    .OrderBy(item => item.Position)
                    .Select(item =>
                    {
                        var product = item.ProductId is { } id && byId.TryGetValue(id, out var found) ? found : null;
                        return new AnalyticsLineFact(
                            item.Position,
                            item.Name,
                            item.Quantity,
                            item.Unit,
                            item.UnitPrice,
                            item.LineTotal,
                            item.IsDiscount,
                            item.DiscountedItemPosition,
                            product?.Id,
                            product?.CanonicalName,
                            product is null
                                ? null
                                : product.NormalizedName + " " + aliasText.GetValueOrDefault(product.Id, ""),
                            product?.CategoryId,
                            product?.Brand,
                            product is null ? null : tagsByProduct.GetValueOrDefault(product.Id));
                    })
                    .ToList()))
            .ToList();
    }

    /// <summary>One spelling per normalized name — the most used one — most frequent names first.</summary>
    private static List<string> MostFrequent(IEnumerable<string> names, int maximum) => names
        .GroupBy(ProductNameNormalizer.Normalize, StringComparer.Ordinal)
        .Where(group => group.Key.Length > 0)
        .OrderByDescending(group => group.Count())
        .ThenBy(group => group.Key, StringComparer.Ordinal)
        .Take(maximum)
        .Select(group => group
            .GroupBy(name => name.Trim(), StringComparer.Ordinal)
            .OrderByDescending(spelling => spelling.Count())
            .ThenBy(spelling => spelling.Key, StringComparer.Ordinal)
            .First().Key)
        .ToList();
}
