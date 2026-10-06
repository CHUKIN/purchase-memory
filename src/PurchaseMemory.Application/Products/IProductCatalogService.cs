namespace PurchaseMemory.Application.Products;

/// <summary>
/// Read side of the product catalog: the searchable list and the single product
/// page. Validation lives here rather than in the endpoint, so the MCP surface
/// and any later caller get the same rules.
/// </summary>
public interface IProductCatalogService
{
    Task<ProductCatalogPage> GetCatalogAsync(
        ProductCatalogQuery query,
        CancellationToken cancellationToken);

    /// <summary>Distinct brands for the filter list, most products first.</summary>
    Task<IReadOnlyList<BrandSummary>> ListBrandsAsync(CancellationToken cancellationToken);

    /// <summary>Returns <c>null</c> when no product carries that identifier.</summary>
    Task<ProductDetail?> GetProductAsync(Guid productId, CancellationToken cancellationToken);
}

public sealed record ProductCatalogQuery
{
    public const int DefaultPageSize = 50;
    public const int MaximumPageSize = 200;
    public const int MaximumQueryLength = 200;

    /// <summary>Free text matched against the product name and its receipt wording.</summary>
    public string? Query { get; init; }

    /// <summary>Products in this category or, for a group, in any of its subcategories.</summary>
    public Guid? CategoryId { get; init; }

    /// <summary>Only products without a category; cannot be combined with <see cref="CategoryId"/>.</summary>
    public bool Uncategorized { get; init; }

    /// <summary>Brand text, matched against the normalized brand.</summary>
    public string? Brand { get; init; }

    /// <summary>Products carrying every one of these tags; empty or <c>null</c> for any.</summary>
    public IReadOnlyCollection<Guid>? TagIds { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = DefaultPageSize;
}
