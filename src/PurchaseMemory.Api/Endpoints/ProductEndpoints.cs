using System.Security.Claims;
using PurchaseMemory.Api.Auth;
using PurchaseMemory.Application.Products;
using PurchaseMemory.Application.Products.Categories;
using PurchaseMemory.Application.Products.Tags;
using PurchaseMemory.Domain.Products;

namespace PurchaseMemory.Api.Endpoints;

/// <summary>
/// The catalog list and product page, plus the changes a person makes to the
/// catalog itself: create by hand or from a barcode, rename, merge two
/// products, settle a «проверить» flag. Data is shared by every approved user
/// in this version (no households), so the approved-user fallback policy is
/// the whole access rule, as for every other data endpoint. None of these
/// calls a paid model, so none is rate limited.
/// </summary>
public static class ProductEndpoints
{
    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/products", GetCatalogAsync)
            .WithName("ListProducts");

        endpoints.MapPost("/api/products", CreateAsync)
            .WithName("CreateProduct");

        endpoints.MapGet("/api/products/{productId:guid}", GetProductAsync)
            .WithName("GetProduct");

        endpoints.MapPut("/api/products/{productId:guid}/name", RenameAsync)
            .WithName("RenameProduct");

        endpoints.MapPost("/api/products/{productId:guid}/merge-into/{targetProductId:guid}", MergeAsync)
            .WithName("MergeProduct");

        endpoints.MapPost("/api/products/{productId:guid}/confirm-distinct", ConfirmDistinctAsync)
            .WithName("ConfirmDistinctProduct");

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
        bool? needsReview,
        Guid? excludeProductId,
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
            NeedsReview = needsReview ?? false,
            ExcludeProductId = excludeProductId,
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
        catch (ProductCategoryNotFoundException exception)
        {
            return Results.Problem(detail: exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (ProductTagNotFoundException exception)
        {
            return Results.Problem(detail: exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        // The flagged-product count rides on this response instead of getting
        // its own endpoint: it is what the catalog's tappable «N товаров нужно
        // проверить» shows, whatever filter the page currently has.
        var needsReviewCount = await repository.CountProductsNeedingReviewAsync(cancellationToken);

        return Results.Ok(ProductCatalogResponse.FromPage(catalogPage, needsReviewCount));
    }

    private static Task<IResult> CreateAsync(
        CreateProductRequest request,
        ClaimsPrincipal user,
        IProductManagementService service,
        CancellationToken cancellationToken) =>
        HandleAsync(async () =>
        {
            var origin = request.Origin?.Trim() switch
            {
                null or "" => ProductOrigin.Manual,
                var value when string.Equals(value, "manual", StringComparison.OrdinalIgnoreCase) => ProductOrigin.Manual,
                var value when string.Equals(value, "barcode", StringComparison.OrdinalIgnoreCase) => ProductOrigin.Barcode,
                _ => throw new ArgumentException("origin must be 'manual' or 'barcode'.")
            };
            var product = await service.CreateAsync(
                new CreateProductCommand(request.Name, request.Brand, request.CategoryId, origin),
                AppUser.Actor(user),
                cancellationToken);
            return Results.Created($"/api/products/{product.Id:D}", ProductSummaryResponse.From(product));
        });

    private static Task<IResult> RenameAsync(
        Guid productId,
        RenameProductRequest request,
        ClaimsPrincipal user,
        IProductManagementService service,
        CancellationToken cancellationToken) =>
        HandleAsync(async () => Results.Ok(ProductSummaryResponse.From(
            await service.RenameAsync(productId, request.Name ?? string.Empty, AppUser.Actor(user), cancellationToken))));

    private static Task<IResult> MergeAsync(
        Guid productId,
        Guid targetProductId,
        ClaimsPrincipal user,
        IProductManagementService service,
        CancellationToken cancellationToken) =>
        HandleAsync(async () =>
        {
            var result = await service.MergeAsync(productId, targetProductId, AppUser.Actor(user), cancellationToken);
            return Results.Ok(new ProductMergeResponse(
                result.SourceProductId,
                result.SourceName,
                result.TargetProductId,
                result.TargetName,
                result.MovedLineCount,
                result.MovedAliasCount));
        });

    private static Task<IResult> ConfirmDistinctAsync(
        Guid productId,
        ClaimsPrincipal user,
        IProductManagementService service,
        CancellationToken cancellationToken) =>
        HandleAsync(async () => Results.Ok(ProductSummaryResponse.From(
            await service.ConfirmDistinctAsync(productId, AppUser.Actor(user), cancellationToken))));

    private static async Task<IResult> HandleAsync(Func<Task<IResult>> operation)
    {
        try
        {
            return await operation();
        }
        catch (ProductNotFoundException exception)
        {
            return Results.Problem(detail: exception.Message, statusCode: StatusCodes.Status404NotFound);
        }
        catch (ProductConflictException exception)
        {
            return Results.Problem(
                detail: exception.Message,
                statusCode: StatusCodes.Status409Conflict,
                extensions: exception.ExistingProductId is { } existing
                    ? new Dictionary<string, object?> { ["existingProductId"] = existing }
                    : null);
        }
        catch (ProductCategoryNotFoundException exception)
        {
            return Results.Problem(detail: exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (ArgumentException exception)
        {
            return Results.Problem(detail: exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    public static string OriginToWire(ProductOrigin origin) => origin switch
    {
        ProductOrigin.Manual => "manual",
        ProductOrigin.Barcode => "barcode",
        _ => "receipt"
    };

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

public sealed record CreateProductRequest(string? Name, string? Brand, Guid? CategoryId, string? Origin);

public sealed record RenameProductRequest(string? Name);

/// <summary>A product right after a change; the page reloads the full detail itself.</summary>
public sealed record ProductSummaryResponse(
    Guid Id,
    string Name,
    string NormalizedName,
    string Origin,
    bool NeedsReview,
    Guid? CategoryId,
    string? CategorySource,
    string? Brand,
    string? BrandSource,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static ProductSummaryResponse From(Product product)
    {
        ArgumentNullException.ThrowIfNull(product);
        return new ProductSummaryResponse(
            product.Id,
            product.CanonicalName,
            product.NormalizedName,
            ProductEndpoints.OriginToWire(product.Origin),
            product.NeedsReview,
            product.CategoryId,
            CategoryEndpoints.SourceToWire(product.CategorySource),
            product.Brand,
            CategoryEndpoints.SourceToWire(product.BrandSource),
            product.CreatedAt,
            product.UpdatedAt);
    }
}

public sealed record ProductMergeResponse(
    Guid SourceProductId,
    string SourceName,
    Guid TargetProductId,
    string TargetName,
    int MovedLineCount,
    int MovedAliasCount);

public sealed record ProductReviewCandidateResponse(
    Guid ProductId,
    string Name,
    string? Brand,
    Guid? CategoryId,
    int PurchaseCount,
    double Score);

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
    IReadOnlyList<TagResponse> Tags,
    bool NeedsReview,
    string Origin);

public sealed record ProductCatalogResponse(
    IReadOnlyList<ProductResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    bool HasMore,
    // Products carrying the «проверить» flag in the whole catalog, whatever the filter.
    int NeedsReviewCount)
{
    public static ProductCatalogResponse FromPage(ProductCatalogPage page, int needsReviewCount)
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
                    (entry.Tags ?? []).Select(TagEndpoints.ToResponse).ToList(),
                    entry.NeedsReview,
                    ProductEndpoints.OriginToWire(entry.Origin)))
                .ToList(),
            page.Page,
            page.PageSize,
            page.TotalCount,
            page.HasMore,
            needsReviewCount);
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
    IReadOnlyList<TagResponse> Tags,
    string Origin,
    bool NeedsReview,
    string? ReviewReason,
    // Flagged products only (empty otherwise): what it might really be, best first.
    IReadOnlyList<ProductReviewCandidateResponse> ReviewCandidates)
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
            (detail.Tags ?? []).Select(TagEndpoints.ToResponse).ToList(),
            ProductEndpoints.OriginToWire(detail.Origin),
            detail.NeedsReview,
            detail.ReviewReason switch
            {
                ProductReviewReason.AmbiguousMatch => "ambiguousMatch",
                _ => null
            },
            (detail.ReviewCandidates ?? [])
                .Select(candidate => new ProductReviewCandidateResponse(
                    candidate.ProductId,
                    candidate.Name,
                    candidate.Brand,
                    candidate.CategoryId,
                    candidate.PurchaseCount,
                    candidate.Score))
                .ToList());
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
