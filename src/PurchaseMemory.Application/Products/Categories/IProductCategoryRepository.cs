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

    /// <summary>An untracked, freshly read product.</summary>
    Task<Product?> FindProductSnapshotAsync(Guid productId, CancellationToken cancellationToken);

    /// <summary>
    /// Untracked snapshots of products among <paramref name="productIds"/> the
    /// model has something to do for: nobody classified them and the
    /// categorizer has not answered for them, or a human name is missing and
    /// the model has not been asked for names yet.
    /// </summary>
    Task<IReadOnlyList<Product>> ListProductsToSuggestAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Untracked snapshots of products that are unclassified or miss a human
    /// name, oldest first. Products the model already answered for (on that
    /// part) are skipped unless <paramref name="includeAttempted"/>.
    /// </summary>
    Task<IReadOnlyList<Product>> ListProductsToSuggestAsync(
        int maximumProducts,
        bool includeAttempted,
        CancellationToken cancellationToken);

    /// <summary>
    /// How many products a backfill would pick up: those the model has not
    /// answered for yet, and (separately) the total when attempted ones are
    /// retried too. Read-only.
    /// </summary>
    Task<PendingSuggestionCounts> CountProductsToSuggestAsync(CancellationToken cancellationToken);

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

    /// <summary>
    /// Writes suggested human names in single conditional statements, so a
    /// person's name typed meanwhile always wins: a name is written only where
    /// nobody decided yet, or, with <paramref name="replaceSuggested"/>, where
    /// it is not confirmed. A <c>null</c> name leaves that language as it is.
    /// Also stamps the naming attempt.
    /// </summary>
    Task<NameSuggestionWrite> ApplyNameSuggestionAsync(
        Guid productId,
        ProductNameSuggestion names,
        bool replaceSuggested,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    void AddAuditEntry(ProductAuditEntry entry);

    Task<IReadOnlyList<ProductAuditEntry>> ListAuditEntriesAsync(Guid productId, CancellationToken cancellationToken);

    /// <summary>
    /// Saves; a row that changed or vanished in between (a product merged
    /// away meanwhile) surfaces as <see cref="ProductCategoryConflictException"/>.
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed record SuggestionWrite(bool CategoryWritten, bool BrandWritten);

/// <summary>Cleaned suggested names with their search keys; <c>null</c> means «no suggestion».</summary>
public sealed record ProductNameSuggestion(
    string? NamePl,
    string? NormalizedNamePl,
    string? NameRu,
    string? NormalizedNameRu)
{
    public static ProductNameSuggestion From(string? namePl, string? nameRu)
    {
        var polish = Product.CleanDisplayName(namePl);
        var russian = Product.CleanDisplayName(nameRu);
        return new ProductNameSuggestion(
            polish,
            polish is null ? null : Product.LimitDisplayName(ProductNameNormalizer.Normalize(polish)),
            russian,
            russian is null ? null : Product.LimitDisplayName(ProductNameNormalizer.Normalize(russian)));
    }
}

public sealed record NameSuggestionWrite(bool PolishWritten, bool RussianWritten);

/// <summary>
/// Products waiting for a backfill. <see cref="Unclassified"/> and
/// <see cref="Unnamed"/> count products not yet attempted (a product can be in
/// both); <see cref="Waiting"/> is the distinct total for a default run and
/// <see cref="WaitingWithRetry"/> the distinct total when retrying attempted ones.
/// </summary>
public sealed record PendingSuggestionCounts(int Unclassified, int Unnamed, int Waiting, int WaitingWithRetry);
