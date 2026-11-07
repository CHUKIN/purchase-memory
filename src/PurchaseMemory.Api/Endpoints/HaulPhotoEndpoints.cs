using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using PurchaseMemory.Api.Auth;
using PurchaseMemory.Application.Products.PhotoNames;
using PurchaseMemory.Application.Purchases.HaulPhoto;

namespace PurchaseMemory.Api.Endpoints;

/// <summary>
/// «Сфотографировать покупки»: one photo of a saved purchase's items → a box
/// and a proposed line per item → the confirmed crops become product photos.
/// Same access rule as every purchase endpoint (the approved-user fallback
/// policy; purchases are the household's shared data, an unknown purchase is
/// 404) and the same photo limits as «Найти по фото». The server keeps
/// nothing between the two calls: the app sends the same photo again with
/// the confirmed pairs.
/// </summary>
public static class HaulPhotoEndpoints
{
    public const int MaximumPairsFieldLength = 16 * 1024;

    private static readonly JsonSerializerOptions PairJson = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapHaulPhotoEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Whether the app shows the button: switched on and configured. Costs nothing.
        endpoints.MapGet("/api/purchases/haul-photo/status", (IHaulPhotoService service) =>
                Results.Ok(new HaulPhotoStatusResponse(service.IsAvailable)))
            .WithName("GetHaulPhotoStatus");

        endpoints.MapPost("/api/purchases/{purchaseId:guid}/haul-photo", ProposeAsync)
            .DisableAntiforgery()
            .WithMetadata(
                new RequestSizeLimitAttribute(ProductEndpoints.MaximumPhotoRequestBytes),
                new RequestFormLimitsAttribute { MultipartBodyLengthLimit = ProductEndpoints.MaximumPhotoRequestBytes })
            .WithName("ProposeHaulPhotoPairs")
            .RequireRateLimiting(ReceiptUploadRateLimiting.HaulPhotoPolicyName);

        // No model call here, only crops and storage: the product photos bucket.
        endpoints.MapPost("/api/purchases/{purchaseId:guid}/haul-photo/save", SaveAsync)
            .DisableAntiforgery()
            .WithMetadata(
                new RequestSizeLimitAttribute(ProductEndpoints.MaximumPhotoRequestBytes),
                new RequestFormLimitsAttribute
                {
                    MultipartBodyLengthLimit = ProductEndpoints.MaximumPhotoRequestBytes,
                    ValueLengthLimit = MaximumPairsFieldLength
                })
            .WithName("SaveHaulPhotoCrops")
            .RequireRateLimiting(ReceiptUploadRateLimiting.ProductPhotosPolicyName);

        return endpoints;
    }

    private static async Task<IResult> ProposeAsync(
        Guid purchaseId,
        HttpRequest request,
        IHaulPhotoService service,
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
            var proposal = await service.ProposeAsync(purchaseId, bytes!, cancellationToken);
            return Results.Ok(HaulPhotoProposalResponse.From(proposal));
        }
        catch (InvalidProductPhotoException exception)
        {
            return ProductEndpoints.InvalidPhotoProblem(exception);
        }
        catch (HaulPhotoDisabledException)
        {
            return Disabled();
        }
        catch (HaulPhotoUnavailableException)
        {
            // The cause (no key, timeout, provider error) is in the log.
            return ProductEndpoints.PhotoProblem(
                StatusCodes.Status503ServiceUnavailable,
                "haulPhotoUnavailable",
                "Recognising the purchased items is unavailable right now. Photos can still be added on each product.");
        }
    }

    private static async Task<IResult> SaveAsync(
        Guid purchaseId,
        HttpRequest request,
        ClaimsPrincipal user,
        IHaulPhotoService service,
        CancellationToken cancellationToken)
    {
        if (!service.IsEnabled)
        {
            return Disabled();
        }

        var (bytes, form, problem) = await ProductEndpoints.ReadOnePhotoWithFormAsync(request, cancellationToken);
        if (problem is not null)
        {
            return problem;
        }

        if (!TryReadPairs(form!, out var pairs))
        {
            return ProductEndpoints.PhotoProblem(
                StatusCodes.Status400BadRequest,
                "pairsInvalid",
                $"Send the confirmed items as a JSON array in a field named pairs: [{{\"lineId\",\"x\",\"y\",\"width\",\"height\"}}], 1 to {HaulPhotoRules.MaximumItems} entries.");
        }

        try
        {
            var result = await service.SaveAsync(purchaseId, bytes!, pairs, AppUser.Actor(user), cancellationToken);
            return Results.Ok(HaulPhotoSaveResponse.From(result));
        }
        catch (InvalidProductPhotoException exception)
        {
            return ProductEndpoints.InvalidPhotoProblem(exception);
        }
        catch (HaulPhotoDisabledException)
        {
            return Disabled();
        }
    }

    private static bool TryReadPairs(IFormCollection form, out IReadOnlyList<HaulPhotoPair> pairs)
    {
        pairs = [];
        var values = form["pairs"];
        if (values.Count != 1 || values[0] is not { } text || text.Length is 0 or > MaximumPairsFieldLength)
        {
            return false;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<List<HaulPhotoPairRequest>>(text, PairJson);
            if (parsed is null || parsed.Count is 0 or > HaulPhotoRules.MaximumItems
                || parsed.Any(pair => pair is null || pair.LineId is null || pair.X is null || pair.Y is null || pair.Width is null || pair.Height is null))
            {
                return false;
            }

            pairs = parsed.Select(pair => new HaulPhotoPair(pair.LineId!.Value, pair.X!.Value, pair.Y!.Value, pair.Width!.Value, pair.Height!.Value)).ToList();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static IResult Disabled() =>
        ProductEndpoints.PhotoProblem(
            StatusCodes.Status503ServiceUnavailable,
            "haulPhotoDisabled",
            "Photos of the purchased items are switched off on this server.");

    private sealed record HaulPhotoPairRequest(Guid? LineId, double? X, double? Y, double? Width, double? Height);
}

public sealed record HaulPhotoStatusResponse(bool Enabled);

/// <param name="X">Left edge as a fraction of the (upright) photo's width; Y, Width and Height likewise.</param>
public sealed record HaulPhotoBoxResponse(double X, double Y, double Width, double Height);

/// <param name="LineId">The proposed purchase line; null when no line fits.</param>
/// <param name="Confidence">"high", "medium" or "low".</param>
public sealed record HaulPhotoItemResponse(int Index, HaulPhotoBoxResponse Box, Guid? LineId, string Confidence);

/// <param name="LineCount">How many product lines the model chose from.</param>
public sealed record HaulPhotoProposalResponse(IReadOnlyList<HaulPhotoItemResponse> Items, int LineCount)
{
    public static HaulPhotoProposalResponse From(HaulPhotoProposal proposal)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        return new HaulPhotoProposalResponse(
            proposal.Items.Select((item, index) => new HaulPhotoItemResponse(
                index,
                new HaulPhotoBoxResponse(item.Box.X, item.Box.Y, item.Box.Width, item.Box.Height),
                item.LineId,
                item.Confidence switch
                {
                    HaulMatchConfidence.High => "high",
                    HaulMatchConfidence.Medium => "medium",
                    _ => "low"
                })).ToList(),
            proposal.LineCount);
    }
}

/// <param name="Status">saved, alreadySaved, noProduct, lineNotFound, invalidBox, photoLimit, productGone or failed.</param>
public sealed record HaulPhotoPairResponse(Guid LineId, string Status, Guid? ProductId, Guid? PhotoId, bool IsMain);

public sealed record HaulPhotoSaveResponse(IReadOnlyList<HaulPhotoPairResponse> Results)
{
    public static HaulPhotoSaveResponse From(HaulPhotoSaveResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new HaulPhotoSaveResponse(result.Results.Select(entry => new HaulPhotoPairResponse(
            entry.LineId,
            JsonNamingPolicy.CamelCase.ConvertName(entry.Status.ToString()),
            entry.ProductId,
            entry.PhotoId,
            entry.IsMain)).ToList());
    }
}
