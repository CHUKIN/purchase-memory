using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using PurchaseMemory.Api.Auth;
using PurchaseMemory.Application.Impressions;
using PurchaseMemory.Domain.Impressions;

namespace PurchaseMemory.Api.Endpoints;

/// <summary>
/// Personal impressions of purchased products. Everything sits behind the
/// approved-user fallback policy; the author is always taken from the token,
/// never from the request, and only the author may withdraw an impression.
/// </summary>
public static class ImpressionEndpoints
{
    /// <summary>One photo of at most 15 MB plus the small text fields.</summary>
    public const long MaximumRequestSizeBytes = 16 * 1024 * 1024;

    public static IEndpointRouteBuilder MapImpressionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/impressions", CreateAsync)
            .DisableAntiforgery()
            .WithMetadata(
                new RequestSizeLimitAttribute(MaximumRequestSizeBytes),
                new RequestFormLimitsAttribute { MultipartBodyLengthLimit = MaximumRequestSizeBytes })
            .WithName("CreateImpression");
        endpoints.MapDelete("/api/impressions/{impressionId:guid}", DeleteAsync).WithName("DeleteImpression");
        endpoints.MapGet("/api/impressions/{impressionId:guid}/photo", GetPhotoAsync).WithName("GetImpressionPhoto");
        endpoints.MapGet("/api/impressions/unrated", ListUnratedAsync).WithName("ListUnratedPurchaseItems");
        endpoints.MapGet("/api/products/{productId:guid}/impressions", ListForProductAsync).WithName("ListProductImpressions");
        endpoints.MapGet("/api/products/{productId:guid}/rateable-lines", ListProductLinesAsync)
            .WithName("ListRateableProductLines");
        endpoints.MapGet("/api/purchases/{purchaseId:guid}/impressions", ListForPurchaseAsync).WithName("ListPurchaseImpressions");
        endpoints.MapGet("/api/purchase-items/{purchaseItemId:guid}/impressions", ListForPurchaseItemAsync)
            .WithName("ListPurchaseItemImpressions");
        return endpoints;
    }

    /// <summary>
    /// <c>multipart/form-data</c>: <c>purchaseItemId</c>, <c>rating</c>
    /// (<c>dislike</c>/<c>neutral</c>/<c>like</c>), optional <c>note</c> and one optional <c>photo</c> file.
    /// </summary>
    private static async Task<IResult> CreateAsync(
        HttpRequest request,
        ClaimsPrincipal user,
        IProductImpressionService service,
        CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType)
        {
            return BadRequest("Send the impression as multipart/form-data.");
        }

        var form = await request.ReadFormAsync(cancellationToken);
        if (!Guid.TryParse(form["purchaseItemId"].ToString(), out var purchaseItemId) || purchaseItemId == Guid.Empty)
        {
            return BadRequest("purchaseItemId must be a purchase line identifier.");
        }

        if (!ImpressionRatingWire.TryParse(form["rating"].ToString(), out var rating))
        {
            return BadRequest("rating must be dislike, neutral or like.");
        }

        if (form.Files.Count > 1 || (form.Files.Count == 1 && form.Files[0].Name != "photo"))
        {
            return BadRequest("Attach at most one file, named photo.");
        }

        var file = form.Files.GetFile("photo");
        await using var photoStream = file?.OpenReadStream();
        try
        {
            var view = await service.CreateAsync(
                new CreateImpressionCommand(
                    purchaseItemId,
                    rating,
                    form["note"].ToString(),
                    photoStream is null ? null : new ImpressionPhotoUpload(photoStream, file!.ContentType)),
                AppUser.Actor(user),
                cancellationToken);
            return Results.Created($"/api/impressions/{view.Id}", ImpressionResponse.From(view));
        }
        catch (ImpressionPurchaseItemNotFoundException exception)
        {
            return Results.Problem(detail: exception.Message, statusCode: StatusCodes.Status404NotFound);
        }
    }

    private static async Task<IResult> DeleteAsync(
        Guid impressionId,
        ClaimsPrincipal user,
        IProductImpressionService service,
        CancellationToken cancellationToken)
    {
        try
        {
            await service.DeleteAsync(impressionId, AppUser.Actor(user), cancellationToken);
            return Results.NoContent();
        }
        catch (ImpressionNotFoundException exception)
        {
            return Results.Problem(detail: exception.Message, statusCode: StatusCodes.Status404NotFound);
        }
        catch (ImpressionAccessDeniedException exception)
        {
            return Results.Problem(detail: exception.Message, statusCode: StatusCodes.Status403Forbidden);
        }
    }

    private static async Task<IResult> GetPhotoAsync(
        Guid impressionId,
        IProductImpressionService service,
        CancellationToken cancellationToken)
    {
        var photo = await service.OpenPhotoAsync(impressionId, cancellationToken);
        return photo is { } found
            ? Results.Stream(found.Content, found.ContentType, enableRangeProcessing: found.Content.CanSeek)
            : Results.Problem(detail: "Impression photo was not found.", statusCode: StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> ListUnratedAsync(
        int? days,
        int? limit,
        ClaimsPrincipal user,
        IProductImpressionService service,
        CancellationToken cancellationToken)
    {
        var items = await service.ListUnratedAsync(Viewer(user), days, limit, cancellationToken);
        return Results.Ok(items.Select(RateablePurchaseItemResponse.From).ToList());
    }

    /// <summary>The product's purchase lines, so the product page can ask «which purchase?».</summary>
    private static async Task<IResult> ListProductLinesAsync(
        Guid productId,
        int? limit,
        IProductImpressionService service,
        CancellationToken cancellationToken)
    {
        var items = await service.ListProductLinesAsync(productId, limit, cancellationToken);
        return Results.Ok(items.Select(RateablePurchaseItemResponse.From).ToList());
    }

    private static async Task<IResult> ListForProductAsync(
        Guid productId,
        ClaimsPrincipal user,
        IProductImpressionService service,
        CancellationToken cancellationToken) =>
        Results.Ok(ToResponses(await service.ListForProductAsync(productId, Viewer(user), cancellationToken)));

    private static async Task<IResult> ListForPurchaseAsync(
        Guid purchaseId,
        ClaimsPrincipal user,
        IProductImpressionService service,
        CancellationToken cancellationToken) =>
        Results.Ok(ToResponses(await service.ListForPurchaseAsync(purchaseId, Viewer(user), cancellationToken)));

    private static async Task<IResult> ListForPurchaseItemAsync(
        Guid purchaseItemId,
        ClaimsPrincipal user,
        IProductImpressionService service,
        CancellationToken cancellationToken) =>
        Results.Ok(ToResponses(await service.ListForPurchaseItemAsync(purchaseItemId, Viewer(user), cancellationToken)));

    private static string Viewer(ClaimsPrincipal user) => AppUser.Actor(user).UserId;

    private static List<ImpressionResponse> ToResponses(IEnumerable<ImpressionView> views) =>
        views.Select(ImpressionResponse.From).ToList();

    private static IResult BadRequest(string detail) =>
        Results.Problem(detail: detail, statusCode: StatusCodes.Status400BadRequest);
}

public static class ImpressionRatingWire
{
    public static string ToWire(ImpressionRating rating) => rating switch
    {
        ImpressionRating.Dislike => "dislike",
        ImpressionRating.Neutral => "neutral",
        ImpressionRating.Like => "like",
        _ => throw new ArgumentOutOfRangeException(nameof(rating))
    };

    public static bool TryParse(string? value, out ImpressionRating rating)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "dislike":
                rating = ImpressionRating.Dislike;
                return true;
            case "neutral":
                rating = ImpressionRating.Neutral;
                return true;
            case "like":
                rating = ImpressionRating.Like;
                return true;
            default:
                rating = default;
                return false;
        }
    }
}

public sealed record ImpressionResponse(
    Guid Id,
    Guid PurchaseId,
    Guid? PurchaseItemId,
    Guid? ProductId,
    string ItemName,
    string Rating,
    string? Note,
    string? PhotoUrl,
    string AuthorUserId,
    string? AuthorEmail,
    bool IsMine,
    DateTimeOffset CreatedAt,
    string? MerchantName,
    DateTimeOffset? PurchasedAt,
    string? Currency,
    decimal? Quantity,
    string? Unit,
    decimal? UnitPrice,
    decimal? LineTotal)
{
    public static ImpressionResponse From(ImpressionView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return new ImpressionResponse(
            view.Id,
            view.PurchaseId,
            view.PurchaseItemId,
            view.ProductId,
            view.ItemName,
            ImpressionRatingWire.ToWire(view.Rating),
            view.Note,
            view.HasPhoto ? $"/api/impressions/{view.Id}/photo" : null,
            view.AuthorUserId,
            view.AuthorEmail,
            view.IsMine,
            view.CreatedAt,
            view.MerchantName,
            view.PurchasedAt,
            view.Currency,
            view.Quantity,
            view.Unit,
            view.UnitPrice,
            view.LineTotal);
    }
}

public sealed record RateablePurchaseItemResponse(
    Guid PurchaseItemId,
    Guid PurchaseId,
    Guid? ProductId,
    string? ProductName,
    string ItemName,
    string? MerchantName,
    DateTimeOffset PurchasedAt,
    string? Currency,
    decimal? UnitPrice,
    decimal? LineTotal)
{
    public static RateablePurchaseItemResponse From(RateablePurchaseItemView item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new RateablePurchaseItemResponse(
            item.PurchaseItemId,
            item.PurchaseId,
            item.ProductId,
            item.ProductName,
            item.ItemName,
            item.MerchantName,
            item.PurchasedAt,
            item.Currency,
            item.UnitPrice,
            item.LineTotal);
    }
}
