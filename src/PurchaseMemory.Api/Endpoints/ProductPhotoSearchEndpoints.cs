using Microsoft.AspNetCore.Mvc;
using PurchaseMemory.Api.Auth;
using PurchaseMemory.Application.Products.PhotoNames;
using PurchaseMemory.Application.Products.PhotoSearch;

namespace PurchaseMemory.Api.Endpoints;

/// <summary>
/// «Найти по фото»: one photo, up to three of the household's own products it
/// most likely shows. Same access rule as every product endpoint (the
/// approved-user fallback policy; the catalog is the household's shared data),
/// and the same photo limits and bucket shape as «Название по фото». The photo
/// is not stored and not logged.
/// </summary>
public static class ProductPhotoSearchEndpoints
{
    public static IEndpointRouteBuilder MapProductPhotoSearchEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Whether the app shows the button: switched on and configured. Costs nothing.
        endpoints.MapGet("/api/products/find-by-photo/status", (IProductPhotoSearchService service) =>
                Results.Ok(new ProductPhotoSearchStatusResponse(service.IsAvailable)))
            .WithName("GetFindProductByPhotoStatus");

        endpoints.MapPost("/api/products/find-by-photo", FindAsync)
            .DisableAntiforgery()
            .WithMetadata(
                new RequestSizeLimitAttribute(ProductEndpoints.MaximumPhotoRequestBytes),
                new RequestFormLimitsAttribute { MultipartBodyLengthLimit = ProductEndpoints.MaximumPhotoRequestBytes })
            .WithName("FindProductByPhoto")
            .RequireRateLimiting(ReceiptUploadRateLimiting.FindByPhotoPolicyName);

        return endpoints;
    }

    private static async Task<IResult> FindAsync(
        HttpRequest request,
        IProductPhotoSearchService service,
        CancellationToken cancellationToken)
    {
        // Switched off: say so before reading the upload.
        if (!service.IsEnabled)
        {
            return Disabled();
        }

        var (bytes, problem) = await ProductEndpoints.ReadOnePhotoAsync(request, cancellationToken);
        if (problem is not null)
        {
            return problem;
        }

        try
        {
            var image = ProductPhotoRules.Check(bytes!);
            var result = await service.FindAsync(image, cancellationToken);
            return Results.Ok(ProductPhotoSearchResponse.From(result));
        }
        catch (InvalidProductPhotoException exception)
        {
            return ProductEndpoints.InvalidPhotoProblem(exception);
        }
        catch (ProductPhotoSearchDisabledException)
        {
            return Disabled();
        }
        catch (ProductPhotoSearchUnavailableException)
        {
            // The cause (no key, timeout, provider error) is in the log.
            return Unavailable();
        }
    }

    private static IResult Disabled() =>
        ProductEndpoints.PhotoProblem(
            StatusCodes.Status503ServiceUnavailable,
            "findByPhotoDisabled",
            "Finding products by photo is switched off on this server.");

    private static IResult Unavailable() =>
        ProductEndpoints.PhotoProblem(
            StatusCodes.Status503ServiceUnavailable,
            "findByPhotoUnavailable",
            "Finding products by photo is unavailable right now. Products can still be found by name.");
}

public sealed record ProductPhotoSearchStatusResponse(bool Enabled);

/// <param name="CandidateCount">How many of the household's products the model chose from.</param>
public sealed record ProductPhotoSearchResponse(IReadOnlyList<ProductPhotoSearchMatchResponse> Matches, int CandidateCount)
{
    public static ProductPhotoSearchResponse From(ProductPhotoSearchResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new ProductPhotoSearchResponse(
            result.Matches.Select(hit => new ProductPhotoSearchMatchResponse(
                hit.ProductId,
                hit.CanonicalName,
                hit.NamePl,
                hit.NameRu,
                hit.Brand,
                hit.Category,
                hit.MainPhotoId,
                hit.PurchaseCount,
                hit.LastPurchasedAt,
                hit.Confidence switch
                {
                    ProductPhotoMatchConfidence.High => "high",
                    ProductPhotoMatchConfidence.Medium => "medium",
                    _ => "low"
                })).ToList(),
            result.CandidateCount);
    }
}

/// <param name="Name">The receipt text (the catalog's canonical name), as in the catalog list.</param>
/// <param name="Confidence">"high", "medium" or "low".</param>
public sealed record ProductPhotoSearchMatchResponse(
    Guid ProductId,
    string Name,
    string? NamePl,
    string? NameRu,
    string? Brand,
    string? Category,
    Guid? MainPhotoId,
    int PurchaseCount,
    DateTimeOffset? LastPurchasedAt,
    string Confidence);
