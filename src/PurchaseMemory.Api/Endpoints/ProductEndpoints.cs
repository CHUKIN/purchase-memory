using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using PurchaseMemory.Api.Auth;
using PurchaseMemory.Application.Products;
using PurchaseMemory.Application.Products.Categories;
using PurchaseMemory.Application.Products.PhotoNames;
using PurchaseMemory.Application.Products.Tags;
using PurchaseMemory.Domain.Products;

namespace PurchaseMemory.Api.Endpoints;

/// <summary>
/// The catalog list and product page, plus the changes a person makes to the
/// catalog itself: create by hand or from a barcode, rename, merge two
/// products, settle a «проверить» flag, edit the human names. Data is shared
/// by every approved user in this version (no households), so the
/// approved-user fallback policy is the whole access rule, as for every other
/// data endpoint. Only «name-suggestions» and «name-from-photo» call a paid
/// model, so only they are rate limited.
/// </summary>
public static class ProductEndpoints
{
    /// <summary>The 1.5 MB photo plus multipart overhead; anything bigger is cut off by the server.</summary>
    public const long MaximumPhotoRequestBytes = 2 * 1024 * 1024;

    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/products", GetCatalogAsync)
            .WithName("ListProducts");

        endpoints.MapPost("/api/products", CreateAsync)
            .WithName("CreateProduct");

        endpoints.MapGet("/api/products/{productId:guid}", GetProductAsync)
            .WithName("GetProduct");

        // Without «language» it renames the receipt-text name (CanonicalName);
        // with «pl» / «ru» it sets that human name, confirmed.
        endpoints.MapPut("/api/products/{productId:guid}/name", RenameAsync)
            .WithName("RenameProduct");

        // «Верно» for the names: every suggested name is confirmed as it is.
        endpoints.MapPost("/api/products/{productId:guid}/names/confirm", ConfirmNamesAsync)
            .WithName("ConfirmProductNames");

        // «Предложить заново»: calls the paid model for this one product, so it
        // has a per-user bucket of its own. Confirmed names are never replaced.
        endpoints.MapPost("/api/products/{productId:guid}/name-suggestions", SuggestNamesAsync)
            .WithName("SuggestProductNames")
            .RequireRateLimiting(ReceiptUploadRateLimiting.NameSuggestionsPolicyName);

        // «Название по фото»: one package photo (multipart field «photo», at most
        // 1.5 MB) read by a paid vision model, a per-user bucket of its own. The
        // photo is not stored and not logged.
        endpoints.MapPost("/api/products/{productId:guid}/name-from-photo", NameFromPhotoAsync)
            .DisableAntiforgery()
            .WithMetadata(
                new RequestSizeLimitAttribute(MaximumPhotoRequestBytes),
                new RequestFormLimitsAttribute { MultipartBodyLengthLimit = MaximumPhotoRequestBytes })
            .WithName("NameProductFromPhoto")
            .RequireRateLimiting(ReceiptUploadRateLimiting.NameFromPhotoPolicyName);

        // «Вернуть» after a photo replaced a name: puts the previous one back.
        endpoints.MapPost("/api/products/{productId:guid}/names/undo-photo", UndoPhotoNameAsync)
            .WithName("UndoProductPhotoName");

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
        IProductCategoryService names,
        CancellationToken cancellationToken) =>
        HandleAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(request.Language))
            {
                return Results.Ok(ProductSummaryResponse.From(
                    await service.RenameAsync(productId, request.Name ?? string.Empty, AppUser.Actor(user), cancellationToken)));
            }

            var language = request.Language.Trim().ToLowerInvariant() switch
            {
                "pl" => ProductNameLanguage.Polish,
                "ru" => ProductNameLanguage.Russian,
                _ => throw new ArgumentException("language must be 'pl' or 'ru'.")
            };
            return Results.Ok(ProductNamesResponse.From(
                await names.SetProductNameAsync(productId, language, request.Name, AppUser.Actor(user), cancellationToken)));
        });

    /// <summary>
    /// Optional body <c>{ namePl, nameRu }</c>: the names on the person's screen.
    /// When given and the product's names differ by now (a new suggestion came
    /// in), nothing is confirmed and the answer is 409.
    /// </summary>
    private static Task<IResult> ConfirmNamesAsync(
        Guid productId,
        ConfirmProductNamesRequest? request,
        ClaimsPrincipal user,
        IProductCategoryService names,
        CancellationToken cancellationToken) =>
        HandleAsync(async () => Results.Ok(ProductNamesResponse.From(
            await names.ConfirmSuggestedNamesAsync(
                productId,
                request is null ? null : new ShownProductNames(request.NamePl, request.NameRu),
                AppUser.Actor(user),
                cancellationToken))));

    private static Task<IResult> SuggestNamesAsync(
        Guid productId,
        IProductCategorizationService categorization,
        CancellationToken cancellationToken) =>
        HandleAsync(async () =>
        {
            try
            {
                return Results.Ok(ProductNamesResponse.From(
                    await categorization.SuggestNamesAsync(productId, cancellationToken)));
            }
            catch (ProductNamingUnavailableException)
            {
                // The cause (no key, timeout, provider error) is in the log; the person only needs to know it failed.
                return Results.Problem(
                    detail: "Name suggestions are unavailable right now. Names can still be typed by hand.",
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    extensions: new Dictionary<string, object?> { ["code"] = "nameSuggestionsUnavailable" });
            }
        });

    private static async Task<IResult> NameFromPhotoAsync(
        Guid productId,
        HttpRequest request,
        ClaimsPrincipal user,
        IProductPhotoNamingService service,
        CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType)
        {
            return PhotoProblem(StatusCodes.Status400BadRequest, "photoMissing", "Send the photo as multipart/form-data in a field named photo.");
        }

        IFormCollection form;
        try
        {
            form = await request.ReadFormAsync(cancellationToken);
        }
        catch (InvalidDataException)
        {
            return PhotoProblem(StatusCodes.Status413PayloadTooLarge, "photoTooLarge", "The photo is too large. Send a compressed JPEG.");
        }

        if (form.Files.Count != 1 || form.Files[0].Name != "photo")
        {
            return PhotoProblem(StatusCodes.Status400BadRequest, "photoMissing", "Attach exactly one file, named photo.");
        }

        var file = form.Files[0];
        if (file.Length > ProductPhotoRules.MaximumBytes)
        {
            return PhotoProblem(StatusCodes.Status413PayloadTooLarge, "photoTooLarge", "The photo is too large. Send a compressed JPEG.");
        }

        byte[] bytes;
        await using (var stream = file.OpenReadStream())
        using (var buffer = new MemoryStream((int)file.Length))
        {
            await stream.CopyToAsync(buffer, cancellationToken);
            bytes = buffer.ToArray();
        }

        return await HandleAsync(async () =>
        {
            try
            {
                var image = ProductPhotoRules.Check(bytes);
                var result = await service.NameFromPhotoAsync(productId, image, AppUser.Actor(user), cancellationToken);
                return Results.Ok(ProductPhotoNameResponse.From(result));
            }
            catch (InvalidProductPhotoException exception)
            {
                return exception.Reason switch
                {
                    InvalidProductPhotoReason.TooLarge => PhotoProblem(StatusCodes.Status413PayloadTooLarge, "photoTooLarge", exception.Message),
                    InvalidProductPhotoReason.NotAnImage => PhotoProblem(StatusCodes.Status415UnsupportedMediaType, "photoNotAnImage", exception.Message),
                    _ => PhotoProblem(StatusCodes.Status400BadRequest, "photoMissing", exception.Message)
                };
            }
            catch (ProductPhotoNamingUnavailableException)
            {
                // The cause (no key, timeout, provider error) is in the log.
                return PhotoProblem(
                    StatusCodes.Status503ServiceUnavailable,
                    "nameFromPhotoUnavailable",
                    "Reading names from photos is unavailable right now. Names can still be typed by hand.");
            }
        });
    }

    private static IResult PhotoProblem(int statusCode, string code, string detail) =>
        Results.Problem(
            detail: detail,
            statusCode: statusCode,
            extensions: new Dictionary<string, object?> { ["code"] = code });

    private static Task<IResult> UndoPhotoNameAsync(
        Guid productId,
        UndoPhotoNameRequest request,
        ClaimsPrincipal user,
        IProductPhotoNamingService service,
        CancellationToken cancellationToken) =>
        HandleAsync(async () => Results.Ok(ProductNamesResponse.From(
            await service.UndoAsync(
                productId,
                ParseLanguage(request.Language),
                request.PhotoName ?? string.Empty,
                request.PreviousName,
                AppUser.Actor(user),
                cancellationToken))));

    internal static ProductNameLanguage ParseLanguage(string? language) =>
        language?.Trim().ToLowerInvariant() switch
        {
            "pl" => ProductNameLanguage.Polish,
            "ru" => ProductNameLanguage.Russian,
            _ => throw new ArgumentException("language must be 'pl' or 'ru'.")
        };

    internal static string LanguageToWire(ProductNameLanguage language) =>
        language == ProductNameLanguage.Polish ? "pl" : "ru";

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
        catch (ProductCategoryConflictException exception)
        {
            return Results.Problem(detail: exception.Message, statusCode: StatusCodes.Status409Conflict);
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

/// <param name="Language"><c>null</c>: the receipt-text name; «pl» / «ru»: that human name (empty clears it).</param>
public sealed record RenameProductRequest(string? Name, string? Language = null);

/// <summary>The names shown when «Верно» was pressed; <c>null</c> for a name that was not there.</summary>
public sealed record ConfirmProductNamesRequest(string? NamePl, string? NameRu);

/// <param name="PhotoName">The name the photo set (it must still be there).</param>
/// <param name="PreviousName">The name before; <c>null</c> = none (the receipt text shows).</param>
public sealed record UndoPhotoNameRequest(string? Language, string? PhotoName, string? PreviousName);

/// <summary>
/// One language after «Название по фото». <c>outcome</c>: «replaced» (the
/// previous name/source are what «Вернуть» puts back), «proposed» (a person's
/// name stays, <c>name</c> is offered), «unchanged» or «notRead».
/// </summary>
public sealed record PhotoNameLanguageResponse(
    string Language,
    string Outcome,
    string? Name,
    string? PreviousName,
    string? PreviousSource);

/// <summary>The names after «Название по фото» plus what happened per language.</summary>
public sealed record ProductPhotoNameResponse(
    Guid ProductId,
    string? NamePl,
    string? NamePlSource,
    string? NameRu,
    string? NameRuSource,
    IReadOnlyList<PhotoNameLanguageResponse> Languages)
{
    public static ProductPhotoNameResponse From(ProductPhotoNamingResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var names = ProductNamesResponse.From(result.Names);
        return new ProductPhotoNameResponse(
            names.ProductId,
            names.NamePl,
            names.NamePlSource,
            names.NameRu,
            names.NameRuSource,
            result.Languages.Select(language => new PhotoNameLanguageResponse(
                ProductEndpoints.LanguageToWire(language.Language),
                language.Outcome switch
                {
                    PhotoNameOutcome.Replaced => "replaced",
                    PhotoNameOutcome.Proposed => "proposed",
                    PhotoNameOutcome.Unchanged => "unchanged",
                    _ => "notRead"
                },
                language.Name,
                language.PreviousName,
                CategoryEndpoints.SourceToWire(language.PreviousSource))).ToList());
    }
}

/// <summary>A product's human names after an edit, confirmation or new suggestion.</summary>
public sealed record ProductNamesResponse(
    Guid ProductId,
    string? NamePl,
    string? NamePlSource,
    string? NameRu,
    string? NameRuSource)
{
    public static ProductNamesResponse From(ProductNamesView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return new ProductNamesResponse(
            view.ProductId,
            view.NamePl,
            CategoryEndpoints.SourceToWire(view.NamePlSource),
            view.NameRu,
            CategoryEndpoints.SourceToWire(view.NameRuSource));
    }
}

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
    DateTimeOffset UpdatedAt,
    string? NamePl = null,
    string? NamePlSource = null,
    string? NameRu = null,
    string? NameRuSource = null)
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
            product.UpdatedAt,
            product.NamePl,
            CategoryEndpoints.SourceToWire(product.NamePlSource),
            product.NameRu,
            CategoryEndpoints.SourceToWire(product.NameRuSource));
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
    double Score,
    string? NamePl = null,
    string? NameRu = null);

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
    string Origin,
    // Human names; null falls back to Name (the receipt text).
    string? NamePl = null,
    string? NamePlSource = null,
    string? NameRu = null,
    string? NameRuSource = null);

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
                    ProductEndpoints.OriginToWire(entry.Origin),
                    entry.Names?.NamePl,
                    CategoryEndpoints.SourceToWire(entry.Names?.NamePlSource),
                    entry.Names?.NameRu,
                    CategoryEndpoints.SourceToWire(entry.Names?.NameRuSource)))
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
    IReadOnlyList<ProductReviewCandidateResponse> ReviewCandidates,
    // Human names; null falls back to Name (the receipt text).
    string? NamePl = null,
    string? NamePlSource = null,
    string? NameRu = null,
    string? NameRuSource = null)
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
                    candidate.Score,
                    candidate.NamePl,
                    candidate.NameRu))
                .ToList(),
            detail.Names?.NamePl,
            CategoryEndpoints.SourceToWire(detail.Names?.NamePlSource),
            detail.Names?.NameRu,
            CategoryEndpoints.SourceToWire(detail.Names?.NameRuSource));
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
