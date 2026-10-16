using Microsoft.EntityFrameworkCore;
using PurchaseMemory.Application.Products.Categories;
using PurchaseMemory.Domain.Products;

namespace PurchaseMemory.Infrastructure.Persistence;

public sealed class ProductCategoryRepository : IProductCategoryRepository
{
    private readonly PurchaseMemoryDbContext _dbContext;

    public ProductCategoryRepository(PurchaseMemoryDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<ProductCategory>> ListCategoriesAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.ProductCategories.ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, int>> CountProductsByCategoryAsync(CancellationToken cancellationToken)
    {
        var rows = await _dbContext.Products
            .AsNoTracking()
            .Where(product => product.CategoryId != null)
            .GroupBy(product => product.CategoryId!.Value)
            .Select(group => new { CategoryId = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(row => row.CategoryId, row => row.Count);
    }

    public void AddCategory(ProductCategory category)
    {
        ArgumentNullException.ThrowIfNull(category);
        _dbContext.ProductCategories.Add(category);
    }

    public void RemoveCategory(ProductCategory category)
    {
        ArgumentNullException.ThrowIfNull(category);
        _dbContext.ProductCategories.Remove(category);
    }

    public async Task<IReadOnlyList<Product>> ListProductsInCategoryAsync(
        Guid categoryId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.Products
            .Where(product => product.CategoryId == categoryId)
            .ToListAsync(cancellationToken);
    }

    public Task<Product?> FindProductForUpdateAsync(Guid productId, CancellationToken cancellationToken)
    {
        return _dbContext.Products.FirstOrDefaultAsync(product => product.Id == productId, cancellationToken);
    }

    public async Task<IReadOnlyList<Product>> ListUnclassifiedProductsAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(productIds);
        var identifiers = productIds.ToList();
        return await _dbContext.Products
            .AsNoTracking()
            .Where(product => identifiers.Contains(product.Id)
                && product.CategorySource == null
                && product.CategorizationAttemptedAt == null)
            .OrderBy(product => product.CreatedAt)
            .ThenBy(product => product.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Product>> ListUnclassifiedProductsAsync(
        int maximumProducts,
        bool includeAttempted,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumProducts);
        return await _dbContext.Products
            .AsNoTracking()
            .Where(product => product.CategorySource == null
                && (includeAttempted || product.CategorizationAttemptedAt == null))
            .OrderBy(product => product.CreatedAt)
            .ThenBy(product => product.Id)
            .Take(maximumProducts)
            .ToListAsync(cancellationToken);
    }

    public async Task<SuggestionWrite> ApplySuggestionAsync(
        Guid productId,
        Guid? categoryId,
        string? brand,
        string? normalizedBrand,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var products = _dbContext.Products.Where(product => product.Id == productId);
        var categoryWritten = false;
        var brandWritten = false;

        if (categoryId is { } category)
        {
            categoryWritten = await products
                .Where(product => (product.CategorySource == null
                        || product.CategorySource != ProductAttributeSource.Confirmed)
                    && (product.CategoryId == null || product.CategoryId != category))
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(product => product.CategoryId, category)
                        .SetProperty(product => product.CategorySource, ProductAttributeSource.Suggested)
                        .SetProperty(product => product.UpdatedAt, now),
                    cancellationToken) > 0;
        }

        var cleanBrand = Product.CleanBrand(brand);
        if (cleanBrand is not null)
        {
            var limitedNormalized = Product.LimitBrand(normalizedBrand);
            brandWritten = await products
                .Where(product => (product.BrandSource == null
                        || product.BrandSource != ProductAttributeSource.Confirmed)
                    && (product.Brand == null || product.Brand != cleanBrand))
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(product => product.Brand, cleanBrand)
                        .SetProperty(product => product.NormalizedBrand, limitedNormalized)
                        .SetProperty(product => product.BrandSource, ProductAttributeSource.Suggested)
                        .SetProperty(product => product.UpdatedAt, now),
                    cancellationToken) > 0;
        }

        // The attempt is recorded whatever the model said, even «no idea», so
        // the product is not paid for again on every save.
        await products.ExecuteUpdateAsync(
            setters => setters.SetProperty(product => product.CategorizationAttemptedAt, now),
            cancellationToken);

        return new SuggestionWrite(categoryWritten, brandWritten);
    }

    public void AddAuditEntry(ProductAuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _dbContext.ProductAuditEntries.Add(entry);
    }

    public async Task<IReadOnlyList<ProductAuditEntry>> ListAuditEntriesAsync(
        Guid productId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.ProductAuditEntries
            .AsNoTracking()
            .Where(entry => entry.ProductId == productId)
            .ToListAsync(cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            throw new ProductCategoryConflictException(
                "The product was changed at the same time. Reload and try again.",
                exception);
        }
    }
}
