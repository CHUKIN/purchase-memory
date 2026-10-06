using Microsoft.EntityFrameworkCore;
using PurchaseMemory.Application.Products;
using PurchaseMemory.Domain.Products;
using PurchaseMemory.Domain.Purchases;

namespace PurchaseMemory.Infrastructure.Persistence;

public sealed class SqliteProductCatalogRepository : IProductCatalogRepository
{
    private readonly PurchaseMemoryDbContext _dbContext;

    public SqliteProductCatalogRepository(PurchaseMemoryDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<ProductAlias>> ListAliasesForMerchantAsync(
        string merchantKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(merchantKey);

        return await _dbContext.ProductAliases
            .AsNoTracking()
            .Where(alias => alias.MerchantKey == merchantKey)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Product>> ListCatalogAsync(
        int maximumProducts,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumProducts);

        return await _dbContext.Products
            .AsNoTracking()
            .OrderBy(product => product.CreatedAt)
            .ThenBy(product => product.Id)
            .Take(maximumProducts)
            .ToListAsync(cancellationToken);
    }

    public void AddProduct(Product product)
    {
        ArgumentNullException.ThrowIfNull(product);
        _dbContext.Products.Add(product);
    }

    public void AddAlias(ProductAlias productAlias)
    {
        ArgumentNullException.ThrowIfNull(productAlias);
        _dbContext.ProductAliases.Add(productAlias);
    }

    public void AddPendingMatch(PendingProductMatch pendingMatch)
    {
        ArgumentNullException.ThrowIfNull(pendingMatch);
        _dbContext.PendingProductMatches.Add(pendingMatch);
    }

    public async Task RemovePendingMatchesAsync(
        IReadOnlyCollection<Guid> purchaseItemIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(purchaseItemIds);

        if (purchaseItemIds.Count == 0)
        {
            return;
        }

        var identifiers = purchaseItemIds.ToList();
        var stale = await _dbContext.PendingProductMatches
            .Where(pending => identifiers.Contains(pending.PurchaseItemId))
            .ToListAsync(cancellationToken);

        if (stale.Count > 0)
        {
            _dbContext.PendingProductMatches.RemoveRange(stale);
        }
    }

    public Task<int> CountPendingMatchesAsync(CancellationToken cancellationToken)
    {
        return _dbContext.PendingProductMatches.CountAsync(cancellationToken);
    }

    public async Task<ProductCatalogPage> ListProductsAsync(
        ProductCatalogFilter filter,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(page);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);

        var products = ApplyClassification(
            _dbContext.Products.AsNoTracking(),
            new ProductClassificationFilter(filter.CategoryIds, filter.Uncategorized, filter.NormalizedBrand, filter.TagIds));

        var normalizedQuery = ProductNameNormalizer.Normalize(filter.Query);
        if (normalizedQuery.Length > 0)
        {
            // Matching the stored normalized form keeps search independent of the
            // SQLite collation, exactly like the purchase feed does. Aliases are
            // searched too: the owner remembers «SER GOUDA PLAST.» from the till,
            // not the canonical name the catalog happened to settle on.
            products = products.Where(product =>
                product.NormalizedName.Contains(normalizedQuery)
                || _dbContext.ProductAliases.Any(alias =>
                    alias.ProductId == product.Id
                    && alias.NormalizedText.Contains(normalizedQuery)));
        }

        var totalCount = await products.CountAsync(cancellationToken);

        var pageItems = await products
            .OrderBy(product => product.NormalizedName)
            .ThenBy(product => product.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(product => new
            {
                product.Id,
                product.CanonicalName,
                product.NormalizedName,
                product.CreatedAt,
                product.UpdatedAt,
                product.CategoryId,
                product.CategorySource,
                product.Brand,
                PurchaseCount = _dbContext.PurchaseDrafts
                    .Count(draft => draft.Items.Any(item => item.ProductId == product.Id)),
                LinkedItemCount = _dbContext.Set<PurchaseItem>().Count(item => item.ProductId == product.Id),
                AliasCount = _dbContext.ProductAliases.Count(alias => alias.ProductId == product.Id)
            })
            .ToListAsync(cancellationToken);

        return new ProductCatalogPage(
            pageItems
                .Select(row => new ProductCatalogEntry(
                    row.Id,
                    row.CanonicalName,
                    row.NormalizedName,
                    row.PurchaseCount,
                    row.LinkedItemCount,
                    row.AliasCount,
                    row.CreatedAt,
                    row.UpdatedAt,
                    row.CategoryId,
                    row.CategorySource,
                    row.Brand))
                .ToList(),
            page,
            pageSize,
            totalCount);
    }

    public async Task<IReadOnlySet<Guid>> ListMatchingProductIdsAsync(
        ProductClassificationFilter filter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var identifiers = await ApplyClassification(_dbContext.Products.AsNoTracking(), filter)
            .Select(product => product.Id)
            .ToListAsync(cancellationToken);
        return identifiers.ToHashSet();
    }

    public async Task<IReadOnlyList<BrandSummary>> ListBrandsAsync(CancellationToken cancellationToken)
    {
        var rows = await _dbContext.Products
            .AsNoTracking()
            .Where(product => product.NormalizedBrand != null && product.Brand != null)
            .GroupBy(product => new { product.NormalizedBrand, product.Brand })
            .Select(group => new { group.Key.NormalizedBrand, group.Key.Brand, Count = group.Count() })
            .ToListAsync(cancellationToken);

        // «Łaciate» and «ŁACIATE» are one brand: grouping by the normalized form
        // happens here, in .NET, so neither database collation decides it. The
        // shown spelling is the most common one, ties broken by ordinal order.
        return rows
            .GroupBy(row => row.NormalizedBrand!, StringComparer.Ordinal)
            .Select(group => new BrandSummary(
                group.OrderByDescending(row => row.Count).ThenBy(row => row.Brand, StringComparer.Ordinal).First().Brand!,
                group.Key,
                group.Sum(row => row.Count)))
            .OrderByDescending(brand => brand.ProductCount)
            .ThenBy(brand => brand.NormalizedBrand, StringComparer.Ordinal)
            .ToList();
    }

    private IQueryable<Product> ApplyClassification(
        IQueryable<Product> products,
        ProductClassificationFilter filter)
    {
        if (filter.CategoryIds is { } categoryIds)
        {
            var identifiers = categoryIds.ToList();
            products = products.Where(product =>
                product.CategoryId != null && identifiers.Contains(product.CategoryId.Value));
        }

        if (filter.Uncategorized)
        {
            products = products.Where(product => product.CategoryId == null);
        }

        if (filter.NormalizedBrand is { } brand)
        {
            products = products.Where(product =>
                product.NormalizedBrand != null && product.NormalizedBrand.Contains(brand));
        }

        // Every chosen tag is required, so each one narrows the set further.
        foreach (var tagId in filter.TagIds ?? [])
        {
            products = products.Where(product =>
                _dbContext.ProductTagLinks.Any(link => link.ProductId == product.Id && link.TagId == tagId));
        }

        return products;
    }

    public Task<ProductAlias?> FindAliasForUpdateAsync(
        string merchantKey,
        string normalizedText,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(merchantKey);
        ArgumentNullException.ThrowIfNull(normalizedText);

        return _dbContext.ProductAliases.FirstOrDefaultAsync(
            alias => alias.MerchantKey == merchantKey && alias.NormalizedText == normalizedText,
            cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetProductNamesAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(productIds);
        if (productIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var identifiers = productIds.Distinct().ToList();
        return await _dbContext.Products
            .AsNoTracking()
            .Where(product => identifiers.Contains(product.Id))
            .ToDictionaryAsync(product => product.Id, product => product.CanonicalName, cancellationToken);
    }

    public async Task<IReadOnlyList<PendingProductMatch>> ListPendingMatchesAsync(
        IReadOnlyCollection<Guid> purchaseItemIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(purchaseItemIds);
        if (purchaseItemIds.Count == 0)
        {
            return [];
        }

        var identifiers = purchaseItemIds.ToList();
        return await _dbContext.PendingProductMatches
            .AsNoTracking()
            .Include(pending => pending.Candidates)
            .Where(pending => identifiers.Contains(pending.PurchaseItemId))
            .ToListAsync(cancellationToken);
    }

    public async Task<Product?> FindProductAsync(Guid productId, CancellationToken cancellationToken)
    {
        return await _dbContext.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(product => product.Id == productId, cancellationToken);
    }

    public async Task<IReadOnlyList<ProductPurchaseFact>> ListPurchaseFactsAsync(
        Guid productId,
        CancellationToken cancellationToken)
    {
        // The purchase moment falls back to the moment the purchase was recorded,
        // matching how the purchase feed orders receipts without a recognized date.
        return await _dbContext.PurchaseDrafts
            .AsNoTracking()
            .SelectMany(draft => draft.Items.Where(item => item.ProductId == productId), (draft, item) =>
                new ProductPurchaseFact(
                    draft.Id,
                    draft.PurchasedAt ?? draft.CreatedAt,
                    draft.MerchantName,
                    draft.Currency,
                    item.Name,
                    item.Quantity,
                    item.Unit,
                    item.UnitPrice,
                    item.LineTotal))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProductAliasView>> ListAliasesForProductAsync(
        Guid productId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.ProductAliases
            .AsNoTracking()
            .Where(alias => alias.ProductId == productId)
            .OrderBy(alias => alias.MerchantKey)
            .ThenBy(alias => alias.RawText)
            .Select(alias => new ProductAliasView(alias.MerchantKey, alias.RawText))
            .ToListAsync(cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
