using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using PurchaseMemory.Api.Endpoints;
using PurchaseMemory.Application.Products;
using PurchaseMemory.Application.Products.Categories;
using PurchaseMemory.Application.Products.Tags;

namespace PurchaseMemory.Api.Mcp;

/// <summary>Read-only access to the product catalog, its categories, brands and tags.</summary>
[McpServerToolType]
public sealed class ProductTools
{
    private readonly IProductCatalogService _catalog;
    private readonly IProductCategoryService _categories;
    private readonly IProductTagService _tags;

    public ProductTools(IProductCatalogService catalog, IProductCategoryService categories, IProductTagService tags)
    {
        _catalog = catalog;
        _categories = categories;
        _tags = tags;
    }

    [McpServerTool(
        Name = "list_product_categories",
        Title = "List product categories",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("The household category tree: groups (parentId null) followed by their subcategories.")]
    public async Task<IReadOnlyList<ProductCategoryView>> ListProductCategoriesAsync(
        CancellationToken cancellationToken = default)
    {
        return await _categories.GetTreeAsync(cancellationToken);
    }

    [McpServerTool(
        Name = "search_products",
        Title = "Search products",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Search catalog products by name or receipt wording, optionally within a category " +
        "(a group includes its subcategories), by brand or by tag. categorySource is suggested (AI) or confirmed (human). " +
        "needsReview marks a product flagged «проверить»: the matcher was unsure whether it is an existing product.")]
    public async Task<IReadOnlyList<McpProductResult>> SearchProductsAsync(
        [Description("Name text to find. Empty lists products alphabetically.")] string query = "",
        [Description("Category id from list_product_categories, or null for any.")] Guid? categoryId = null,
        [Description("Brand text, or empty for any. Case and Polish diacritics are ignored.")] string brand = "",
        [Description("Only products without a category. Cannot be combined with categoryId.")] bool uncategorized = false,
        [Description("Tag id from list_tags, or null for any.")] Guid? tagId = null,
        [Description("Only products flagged «проверить».")] bool needsReview = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var page = await _catalog.GetCatalogAsync(
                new ProductCatalogQuery
                {
                    Query = query,
                    CategoryId = categoryId,
                    Uncategorized = uncategorized,
                    Brand = brand,
                    TagIds = tagId is { } id ? [id] : null,
                    NeedsReview = needsReview,
                    PageSize = 50
                },
                cancellationToken);
            return page.Items
                .Select(item => new McpProductResult(
                    item.ProductId,
                    item.CanonicalName,
                    item.CategoryId,
                    CategoryEndpoints.SourceToWire(item.CategorySource),
                    item.Brand,
                    (item.Tags ?? []).Select(tag => tag.Name).ToList(),
                    item.PurchaseCount,
                    item.NeedsReview))
                .ToList();
        }
        catch (ArgumentException exception)
        {
            throw new McpException(exception.Message, exception);
        }
        catch (ProductCategoryNotFoundException exception)
        {
            throw new McpException(exception.Message, exception);
        }
        catch (ProductTagNotFoundException exception)
        {
            throw new McpException(exception.Message, exception);
        }
    }

    [McpServerTool(
        Name = "list_brands",
        Title = "List brands",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Distinct product brands with how many products carry each, most common first.")]
    public async Task<IReadOnlyList<BrandResponse>> ListBrandsAsync(CancellationToken cancellationToken = default)
    {
        var brands = await _catalog.ListBrandsAsync(cancellationToken);
        return brands.Select(brand => new BrandResponse(brand.Brand, brand.ProductCount)).ToList();
    }

    [McpServerTool(
        Name = "list_tags",
        Title = "List tags",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Household tags (for example «для ребёнка», «к празднику») with how many products carry each, " +
        "alphabetically. A product has one category but any number of tags.")]
    public async Task<IReadOnlyList<TagSummaryResponse>> ListTagsAsync(CancellationToken cancellationToken = default)
    {
        var tags = await _tags.ListAsync(cancellationToken);
        return tags.Select(tag => new TagSummaryResponse(tag.Id, tag.Name, tag.ProductCount)).ToList();
    }
}

public sealed record McpProductResult(
    Guid Id,
    string Name,
    Guid? CategoryId,
    string? CategorySource,
    string? Brand,
    IReadOnlyList<string> Tags,
    int PurchaseCount,
    bool NeedsReview);
