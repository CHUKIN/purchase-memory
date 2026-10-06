using PurchaseMemory.Application.Products;
using PurchaseMemory.Application.Products.Tags;

namespace PurchaseMemory.Api.Endpoints;

public static class ProductEndpoints
{
    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/products", GetCatalogAsync)
            .WithName("ListProducts");

        endpoints.MapGet("/api/products/{productId:guid}", GetProductAsync)
            .WithName("GetProduct");

        endpoints.MapGet("/api/brands", ListBrandsAsync)
            .WithName("ListBrands");

        return endpoints;
    }

    private static async Task<IResult> ListBrandsAsync(
        IProductCatalogService catalog,
        CancellationToken cancellationToken)
    {
        var brands = await catalog.ListBrandsAsync(cancellationToken);
        return Results.Ok(brands.Select(brand => new BrandResponse(brand.Brand, brand.ProductCount)).ToList());
    }

    private static async Task<IResult> GetCatalogAsync(
        string? q,
        Guid? categoryId,
        bool? uncategorized,
        string? brand,
        string[]? tagId,
        int? page,
        int? pageSize,
        IProductCatalogService catalog,
        IProductCatalogRepository repository,
        CancellationToken cancellationToken)
    {
        if (!TagEndpoints.TryParseTagIds(tagId, out var tagIds))
        {
            return Results.Problem(detail: TagEndpoints.InvalidTagIdMessage, statusCode: StatusCodes.Status400BadRequest);
        }

        var query = new ProductCatalogQuery
        {
            Query = q,
            CategoryId = categoryId,
            Uncategorized = uncategorized ?? false,
            Brand = brand,
            TagIds = tagIds,
            Page = page ?? 1,
            PageSize = pageSize ?? ProductCatalogQuery.DefaultPageSize
        };

        ProductCatalogPage catalogPage;
        try
        {
            catalogPage = await catalog.GetCatalogAsync(query, cancellationToken);
        }
        catch (ArgumentException exception)
        {
            return Results.Problem(detail: exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (PurchaseMemory.Application.Products.Categories.ProductCategoryNotFoundException exception)
        {
            return Results.Problem(detail: exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (ProductTagNotFoundException exception)
        {
            return Results.Problem(detail: exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        // The pending-match count rides on this response instead of getting its
        // own endpoint: it is the same question («did matching work, and what is
        // left over?»), one query, and the future review queue can still read it
        // here before it grows a screen of its own.
        var pendingMatchCount = await repository.CountPendingMatchesAsync(cancellationToken);

        return Results.Ok(ProductCatalogResponse.FromPage(catalogPage, pendingMatchCount));
    }

    private static async Task<IResult> GetProductAsync(
        Guid productId,
        IProductCatalogService catalog,
        CancellationToken cancellationToken)
    {
        var product = await catalog.GetProductAsync(productId, cancellationToken);
        return product is null
            ? Results.Problem(detail: "Product was not found.", statusCode: StatusCodes.Status404NotFound)
            : Results.Ok(ProductDetailResponse.FromDetail(product));
    }
}

public sealed record BrandResponse(string Brand, int ProductCount);

public sealed record ProductResponse(
    Guid Id,
    string Name,
    string NormalizedName,
    int PurchaseCount,
    int LinkedItemCount,
    int AliasCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid? CategoryId,
    string? CategorySource,
    string? Brand,
    IReadOnlyList<TagResponse> Tags);

public sealed record ProductCatalogResponse(
    IReadOnlyList<ProductResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    bool HasMore,
    int PendingMatchCount)
{
    public static ProductCatalogResponse FromPage(ProductCatalogPage page, int pendingMatchCount)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new ProductCatalogResponse(
            page.Items
                .Select(entry => new ProductResponse(
                    entry.ProductId,
                    entry.CanonicalName,
                    entry.NormalizedName,
                    entry.PurchaseCount,
                    entry.LinkedItemCount,
                    entry.AliasCount,
                    entry.CreatedAt,
                    entry.UpdatedAt,
                    entry.CategoryId,
                    CategoryEndpoints.SourceToWire(entry.CategorySource),
                    entry.Brand,
                    (entry.Tags ?? []).Select(TagEndpoints.ToResponse).ToList()))
                .ToList(),
            page.Page,
            page.PageSize,
            page.TotalCount,
            page.HasMore,
            pendingMatchCount);
    }
}

public sealed record ProductPricePointResponse(
    DateTimeOffset PurchasedAt,
    string? MerchantName,
    decimal UnitPrice,
    Guid PurchaseId);

public sealed record ProductPriceSeriesResponse(
    string? Currency,
    string Unit,
    decimal MinimumPrice,
    decimal AveragePrice,
    decimal LatestPrice,
    ProductPricePointResponse LatestPoint,
    IReadOnlyList<ProductPricePointResponse> Points);

public sealed record ProductFrequencyResponse(
    DateTimeOffset FirstPurchasedAt,
    DateTimeOffset LastPurchasedAt,
    int PurchaseCount,
    double? AverageDaysBetweenPurchases);

public sealed record ProductAliasResponse(string MerchantKey, string RawText);

public sealed record ProductDetailResponse(
    Guid Id,
    string Name,
    string NormalizedName,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int PurchaseCount,
    int LinkedItemCount,
    int UnpricedItemCount,
    IReadOnlyList<ProductPriceSeriesResponse> PriceSeries,
    ProductFrequencyResponse? Frequency,
    IReadOnlyList<ProductAliasResponse> Aliases,
    Guid? CategoryId,
    string? CategorySource,
    string? Brand,
    string? BrandSource,
    IReadOnlyList<TagResponse> Tags)
{
    public static ProductDetailResponse FromDetail(ProductDetail detail)
    {
        ArgumentNullException.ThrowIfNull(detail);

        return new ProductDetailResponse(
            detail.ProductId,
            detail.CanonicalName,
            detail.NormalizedName,
            detail.CreatedAt,
            detail.UpdatedAt,
            detail.PurchaseCount,
            detail.LinkedItemCount,
            detail.UnpricedItemCount,
            detail.PriceSeries.Select(ToResponse).ToList(),
            detail.Frequency is null
                ? null
                : new ProductFrequencyResponse(
                    detail.Frequency.FirstPurchasedAt,
                    detail.Frequency.LastPurchasedAt,
                    detail.Frequency.PurchaseCount,
                    detail.Frequency.AverageDaysBetweenPurchases),
            detail.Aliases.Select(alias => new ProductAliasResponse(alias.MerchantKey, alias.RawText)).ToList(),
            detail.CategoryId,
            CategoryEndpoints.SourceToWire(detail.CategorySource),
            detail.Brand,
            CategoryEndpoints.SourceToWire(detail.BrandSource),
            (detail.Tags ?? []).Select(TagEndpoints.ToResponse).ToList());
    }

    private static ProductPriceSeriesResponse ToResponse(ProductPriceSeries series)
    {
        return new ProductPriceSeriesResponse(
            series.Currency,
            series.Unit,
            series.MinimumPrice,
            series.AveragePrice,
            series.LatestPrice,
            ToResponse(series.LatestPoint),
            series.Points.Select(ToResponse).ToList());
    }

    private static ProductPricePointResponse ToResponse(ProductPricePoint point)
    {
        return new ProductPricePointResponse(
            point.PurchasedAt,
            point.MerchantName,
            point.UnitPrice,
            point.PurchaseId);
    }
}
