using PurchaseMemory.Application.Products.Categories;
using PurchaseMemory.Application.Products.Tags;

namespace PurchaseMemory.Application.Products;

public sealed class ProductCatalogService : IProductCatalogService
{
    private readonly IProductCatalogRepository _repository;
    private readonly IProductCategoryRepository _categories;
    private readonly IProductTagRepository _tags;

    public ProductCatalogService(
        IProductCatalogRepository repository,
        IProductCategoryRepository categories,
        IProductTagRepository tags)
    {
        _repository = repository;
        _categories = categories;
        _tags = tags;
    }

    public async Task<ProductCatalogPage> GetCatalogAsync(
        ProductCatalogQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var text = query.Query?.Trim();
        if (text?.Length > ProductCatalogQuery.MaximumQueryLength)
        {
            throw new ArgumentException(
                $"Search query cannot exceed {ProductCatalogQuery.MaximumQueryLength} characters.",
                nameof(query));
        }

        if (query.Page < 1)
        {
            throw new ArgumentException("Page must be 1 or greater.", nameof(query));
        }

        if (query.PageSize is < 1 || query.PageSize > ProductCatalogQuery.MaximumPageSize)
        {
            throw new ArgumentException(
                $"Page size must be between 1 and {ProductCatalogQuery.MaximumPageSize}.",
                nameof(query));
        }

        var classification = await ProductClassificationFilter.ResolveAsync(
            query.CategoryId, query.Uncategorized, query.Brand, query.TagIds, _categories, _tags, cancellationToken);
        var filter = new ProductCatalogFilter(
            text,
            classification.CategoryIds,
            classification.Uncategorized,
            classification.NormalizedBrand,
            classification.TagIds,
            query.NeedsReview,
            query.ExcludeProductId);
        var page = await _repository.ListProductsAsync(filter, query.Page, query.PageSize, cancellationToken);

        var tags = await _tags.ListTagsForProductsAsync(
            page.Items.Select(item => item.ProductId).ToList(),
            cancellationToken);
        return page with
        {
            Items = page.Items
                .Select(item => item with { Tags = tags.TryGetValue(item.ProductId, out var found) ? found : [] })
                .ToList()
        };
    }

    public Task<IReadOnlyList<BrandSummary>> ListBrandsAsync(CancellationToken cancellationToken)
    {
        return _repository.ListBrandsAsync(cancellationToken);
    }

    public async Task<ProductDetail?> GetProductAsync(Guid productId, CancellationToken cancellationToken)
    {
        if (productId == Guid.Empty)
        {
            throw new ArgumentException("Product identifier cannot be empty.", nameof(productId));
        }

        var product = await _repository.FindProductAsync(productId, cancellationToken);
        if (product is null)
        {
            return null;
        }

        var facts = await _repository.ListPurchaseFactsAsync(productId, cancellationToken);
        var aliases = await _repository.ListAliasesForProductAsync(productId, cancellationToken);
        var summary = ProductPriceHistoryCalculator.Summarize(facts);
        var tags = await _tags.ListTagsForProductsAsync([product.Id], cancellationToken);
        var candidates = product.NeedsReview
            ? await _repository.ListReviewCandidatesAsync([product.Id], cancellationToken)
            : null;

        return new ProductDetail(
            product.Id,
            product.CanonicalName,
            product.NormalizedName,
            product.CreatedAt,
            product.UpdatedAt,
            facts.Select(fact => fact.PurchaseId).Distinct().Count(),
            facts.Count,
            summary.Series,
            summary.UnpricedItemCount,
            summary.Frequency,
            aliases,
            product.CategoryId,
            product.CategorySource,
            product.Brand,
            product.BrandSource,
            tags.TryGetValue(product.Id, out var found) ? found : [],
            product.Origin,
            product.NeedsReview,
            product.ReviewReason,
            candidates is not null && candidates.TryGetValue(product.Id, out var reviewCandidates) ? reviewCandidates : [],
            ProductDisplayNames.Of(product));
    }
}
