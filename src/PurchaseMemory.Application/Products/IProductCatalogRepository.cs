using PurchaseMemory.Application.Products.Tags;
using PurchaseMemory.Domain.Products;

namespace PurchaseMemory.Application.Products;

/// <summary>
/// Persistence boundary for the product catalog, mirroring the style of
/// <see cref="Purchases.IPurchaseDraftRepository"/>.
/// </summary>
public interface IProductCatalogRepository
{
    /// <summary>Aliases recorded for one merchant, keyed by normalized receipt text.</summary>
    Task<IReadOnlyList<ProductAlias>> ListAliasesForMerchantAsync(
        string merchantKey,
        CancellationToken cancellationToken);

    /// <summary>
    /// Whole catalog, capped by <paramref name="maximumProducts"/>. A household
    /// catalog is measured in hundreds of rows, so scoring it in memory is both
    /// simpler and faster than pushing similarity into SQLite.
    /// </summary>
    Task<IReadOnlyList<Product>> ListCatalogAsync(
        int maximumProducts,
        CancellationToken cancellationToken);

    void AddProduct(Product product);

    void AddAlias(ProductAlias productAlias);

    /// <summary>The candidates of a product the matcher flagged for review.</summary>
    void AddReviewCandidates(IReadOnlyCollection<ProductReviewCandidate> candidates);

    void AddAuditEntry(ProductAuditEntry entry);

    /// <summary>Products carrying the «проверить» flag, for the catalog's tappable hint.</summary>
    Task<int> CountProductsNeedingReviewAsync(CancellationToken cancellationToken);

    /// <summary>
    /// One page of the catalog. <paramref name="query"/> is matched against both
    /// the canonical name and the recorded receipt wording, because the owner
    /// searches for the product the way the till printed it.
    /// </summary>
    Task<ProductCatalogPage> ListProductsAsync(
        ProductCatalogFilter filter,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    /// <summary>
    /// Identifiers of every product matching the category, brand and tag filter,
    /// for callers such as the purchase feed that filter by product links.
    /// </summary>
    Task<IReadOnlySet<Guid>> ListMatchingProductIdsAsync(
        ProductClassificationFilter filter,
        CancellationToken cancellationToken);

    /// <summary>Distinct brands (by normalized form) with how many products carry each.</summary>
    Task<IReadOnlyList<BrandSummary>> ListBrandsAsync(CancellationToken cancellationToken);

    Task<Product?> FindProductAsync(Guid productId, CancellationToken cancellationToken);

    /// <summary>
    /// The lines linked to this one product, filtered in SQL. A household buys a
    /// given product tens of times, so those few rows are summarized in memory
    /// rather than aggregated in SQL.
    /// </summary>
    Task<IReadOnlyList<ProductPurchaseFact>> ListPurchaseFactsAsync(
        Guid productId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ProductAliasView>> ListAliasesForProductAsync(
        Guid productId,
        CancellationToken cancellationToken);

    /// <summary>The tracked alias for one store and wording, so a person's choice can repoint it.</summary>
    Task<ProductAlias?> FindAliasForUpdateAsync(
        string merchantKey,
        string normalizedText,
        CancellationToken cancellationToken);

    /// <summary>Canonical names of the given products; unknown identifiers are simply absent.</summary>
    Task<IReadOnlyDictionary<Guid, string>> GetProductNamesAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Review candidates of each given flagged product, best first, with the
    /// candidate's name, brand, category and purchase count. Candidates whose
    /// product no longer exists are left out; products without any are absent.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<ProductReviewCandidateView>>> ListReviewCandidatesAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken);

    /// <summary>Which of the given products carry the «проверить» flag.</summary>
    Task<IReadOnlySet<Guid>> ListProductsNeedingReviewAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>One catalog row with how much history stands behind it.</summary>
public sealed record ProductCatalogEntry(
    Guid ProductId,
    string CanonicalName,
    string NormalizedName,
    // Distinct purchases the product appears on — shopping trips, not receipt
    // lines. This is the number the catalog shows, so that it cannot disagree
    // with the frequency the product page states from the same definition.
    int PurchaseCount,
    // Receipt lines linked to the product. Two jars of yoghurt on one receipt
    // are two lines and one purchase; kept for callers that need the raw count.
    int LinkedItemCount,
    int AliasCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid? CategoryId = null,
    ProductAttributeSource? CategorySource = null,
    string? Brand = null,
    // Filled by the catalog service from the tag links; the repository leaves it empty.
    IReadOnlyList<ProductTagSummary>? Tags = null,
    bool NeedsReview = false,
    ProductOrigin Origin = ProductOrigin.Receipt);

/// <summary>
/// Already validated catalog filters. <see cref="CategoryIds"/> is the chosen
/// category plus its subcategories; <c>null</c> means «any category».
/// </summary>
public sealed record ProductCatalogFilter(
    string? Query,
    IReadOnlyCollection<Guid>? CategoryIds = null,
    bool Uncategorized = false,
    string? NormalizedBrand = null,
    IReadOnlyCollection<Guid>? TagIds = null,
    // Only products carrying the «проверить» flag.
    bool NeedsReview = false,
    // Leaves this product out (the «Объединить с…» picker must not offer the product itself).
    Guid? ExcludeProductId = null);

public sealed record ProductCatalogPage(
    IReadOnlyList<ProductCatalogEntry> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    public bool HasMore => Page * PageSize < TotalCount;
}

/// <summary>
/// One brand for the filter list. <see cref="Brand"/> is the most common
/// spelling among products sharing <see cref="NormalizedBrand"/>.
/// </summary>
public sealed record BrandSummary(string Brand, string NormalizedBrand, int ProductCount);
