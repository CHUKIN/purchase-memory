using PurchaseMemory.Domain.Products;

namespace PurchaseMemory.Application.Products.Categories;

/// <summary>Persistence boundary for the category tree and product classification.</summary>
public interface IProductCategoryRepository
{
    /// <summary>Every category, tracked, so callers may rename or delete them.</summary>
    Task<IReadOnlyList<ProductCategory>> ListCategoriesAsync(CancellationToken cancellationToken);

    /// <summary>How many products sit directly in each category.</summary>
    Task<IReadOnlyDictionary<Guid, int>> CountProductsByCategoryAsync(CancellationToken cancellationToken);

    void AddCategory(ProductCategory category);

    void RemoveCategory(ProductCategory category);

    /// <summary>Tracked products currently in the category.</summary>
    Task<IReadOnlyList<Product>> ListProductsInCategoryAsync(Guid categoryId, CancellationToken cancellationToken);

    Task<Product?> FindProductForUpdateAsync(Guid productId, CancellationToken cancellationToken);

    /// <summary>
    /// Untracked snapshots of products among <paramref name="productIds"/> nobody
    /// has classified yet and the categorizer has not answered for.
    /// </summary>
    Task<IReadOnlyList<Product>> ListUnclassifiedProductsAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Untracked snapshots of unclassified products, oldest first. Products the
    /// categorizer already answered for are skipped unless <paramref name="includeAttempted"/>.
    /// </summary>
    Task<IReadOnlyList<Product>> ListUnclassifiedProductsAsync(
        int maximumProducts,
        bool includeAttempted,
        CancellationToken cancellationToken);

    /// <summary>
    /// Writes a suggestion in single conditional statements, so a person's
    /// confirmation made while the model was thinking always wins: the category
    /// changes only where it is not confirmed, the brand likewise. Also stamps
    /// the attempt. Returns what was actually written.
    /// </summary>
    Task<SuggestionWrite> ApplySuggestionAsync(
        Guid productId,
        Guid? categoryId,
        string? brand,
        string? normalizedBrand,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    void AddAuditEntry(ProductAuditEntry entry);

    Task<IReadOnlyList<ProductAuditEntry>> ListAuditEntriesAsync(Guid productId, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed record SuggestionWrite(bool CategoryWritten, bool BrandWritten);
