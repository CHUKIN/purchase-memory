using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using PurchaseMemory.Api.Auth;
using PurchaseMemory.Application.Products.Categories;
using PurchaseMemory.Application.Products.Media;
using PurchaseMemory.Application.Products.PhotoNames;
using PurchaseMemory.Domain.Products;

namespace PurchaseMemory.Api.Endpoints;

/// <summary>
/// Product photos, barcodes, «Больше не покупать», codes remembered for later
/// and the Open Food Facts lookup. Like all data here they are the household's
/// (every approved user sees and changes the same), so the approved-user
/// fallback policy is the access rule. Photos are served only through these
/// authenticated endpoints, never by public storage URLs.
/// </summary>
public static class ProductMediaEndpoints
{
    public static IEndpointRouteBuilder MapProductMediaEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/products/{productId:guid}/media", GetMediaAsync).WithName("GetProductMedia");

        // One package photo (multipart field «photo», at most 1.5 MB, JPEG/PNG/WebP)
        // and an optional «clientId» that makes a repeated upload return the first.
        endpoints.MapPost("/api/products/{productId:guid}/photos", AddPhotoAsync)
            .DisableAntiforgery()
            .WithMetadata(
                new RequestSizeLimitAttribute(ProductEndpoints.MaximumPhotoRequestBytes),
                new RequestFormLimitsAttribute { MultipartBodyLengthLimit = ProductEndpoints.MaximumPhotoRequestBytes })
            .WithName("AddProductPhoto")
            .RequireRateLimiting(ReceiptUploadRateLimiting.ProductPhotosPolicyName);
        endpoints.MapDelete("/api/products/{productId:guid}/photos/{photoId:guid}", RemovePhotoAsync).WithName("DeleteProductPhoto");
        endpoints.MapPut("/api/products/{productId:guid}/photos/{photoId:guid}/main", SetMainPhotoAsync).WithName("SetMainProductPhoto");
        endpoints.MapGet("/api/product-photos/{photoId:guid}", (Guid photoId, HttpContext context, IProductMediaService service, CancellationToken cancellationToken) =>
            ServePhotoAsync(photoId, thumbnail: false, context, service, cancellationToken)).WithName("GetProductPhoto");
        endpoints.MapGet("/api/product-photos/{photoId:guid}/thumbnail", (Guid photoId, HttpContext context, IProductMediaService service, CancellationToken cancellationToken) =>
            ServePhotoAsync(photoId, thumbnail: true, context, service, cancellationToken)).WithName("GetProductPhotoThumbnail");

        endpoints.MapPost("/api/products/{productId:guid}/barcodes", AddBarcodeAsync).WithName("AddProductBarcode");
        endpoints.MapDelete("/api/products/{productId:guid}/barcodes/{code}", RemoveBarcodeAsync).WithName("DeleteProductBarcode");
        endpoints.MapPut("/api/products/{productId:guid}/avoid", SetAvoidAsync).WithName("SetProductAvoid");

        endpoints.MapGet("/api/barcodes/{code}", FindByBarcodeAsync).WithName("FindProductsByBarcode");
        endpoints.MapGet("/api/remembered-barcodes", ListRememberedAsync).WithName("ListRememberedBarcodes");
        endpoints.MapPut("/api/remembered-barcodes/{code}", RememberAsync).WithName("RememberBarcode");
        endpoints.MapDelete("/api/remembered-barcodes/{code}", ForgetAsync).WithName("ForgetBarcode");

        endpoints.MapGet("/api/open-food-facts/{code}", LookupOpenFoodFactsAsync)
            .WithName("LookupOpenFoodFacts")
            .RequireRateLimiting(ReceiptUploadRateLimiting.OpenFoodFactsPolicyName);
        return endpoints;
    }

    private static Task<IResult> GetMediaAsync(Guid productId, IProductMediaService service, CancellationToken cancellationToken) =>
        HandleAsync(async () => Results.Ok(ProductMediaResponse.From(await service.GetAsync(productId, cancellationToken))));

    private static async Task<IResult> AddPhotoAsync(
        Guid productId,
        HttpRequest request,
        ClaimsPrincipal user,
        IProductMediaService service,
        CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType)
        {
            return Problem(StatusCodes.Status400BadRequest, "photoMissing", "Send the photo as multipart/form-data in a field named photo.");
        }

        IFormCollection form;
        try
        {
            form = await request.ReadFormAsync(cancellationToken);
        }
        catch (InvalidDataException)
        {
            return Problem(StatusCodes.Status413PayloadTooLarge, "photoTooLarge", "The photo is too large. Send a compressed JPEG.");
        }

        if (form.Files.Count != 1 || form.Files[0].Name != "photo")
        {
            return Problem(StatusCodes.Status400BadRequest, "photoMissing", "Attach exactly one file, named photo.");
        }

        var file = form.Files[0];
        if (file.Length > ProductPhotoRules.MaximumBytes)
        {
            return Problem(StatusCodes.Status413PayloadTooLarge, "photoTooLarge", "The photo is too large. Send a compressed JPEG.");
        }

        byte[] bytes;
        await using (var stream = file.OpenReadStream())
        using (var buffer = new MemoryStream((int)file.Length))
        {
            await stream.CopyToAsync(buffer, cancellationToken);
            bytes = buffer.ToArray();
        }

        var clientId = form["clientId"].ToString();
        return await HandleAsync(async () =>
        {
            var result = await service.AddPhotoAsync(productId, bytes, clientId, AppUser.Actor(user), cancellationToken);
            var body = new ProductPhotoUploadResponse(ProductPhotoResponse.From(result.Photo), result.Created, result.FirstPhoto);
            return result.Created
                ? Results.Created(ProductPhotoResponse.UrlOf(result.Photo.Id), body)
                : Results.Ok(body);
        });
    }

    private static Task<IResult> RemovePhotoAsync(
        Guid productId,
        Guid photoId,
        ClaimsPrincipal user,
        IProductMediaService service,
        CancellationToken cancellationToken) =>
        HandleAsync(async () =>
        {
            await service.RemovePhotoAsync(productId, photoId, AppUser.Actor(user), cancellationToken);
            return Results.NoContent();
        });

    private static Task<IResult> SetMainPhotoAsync(
        Guid productId,
        Guid photoId,
        ClaimsPrincipal user,
        IProductMediaService service,
        CancellationToken cancellationToken) =>
        HandleAsync(async () => Results.Ok(ProductMediaResponse.From(
            await service.SetMainPhotoAsync(productId, photoId, AppUser.Actor(user), cancellationToken))));

    private static async Task<IResult> ServePhotoAsync(
        Guid photoId,
        bool thumbnail,
        HttpContext context,
        IProductMediaService service,
        CancellationToken cancellationToken)
    {
        var photo = await service.OpenPhotoAsync(photoId, thumbnail, cancellationToken);
        if (photo is null)
        {
            return Results.Problem(detail: "Photo was not found.", statusCode: StatusCodes.Status404NotFound);
        }

        // The bytes behind a photo id never change; a private cache spares the
        // phone a download per list row. «private»: no shared proxy keeps it.
        context.Response.Headers.CacheControl = "private, max-age=86400";
        return Results.Stream(
            photo.Content,
            photo.ContentType,
            entityTag: new EntityTagHeaderValue($"\"{photoId:N}{(thumbnail ? "t" : "p")}\""),
            enableRangeProcessing: false);
    }

    private static Task<IResult> AddBarcodeAsync(
        Guid productId,
        AddBarcodeRequest request,
        ClaimsPrincipal user,
        IProductMediaService service,
        CancellationToken cancellationToken) =>
        HandleAsync(async () =>
        {
            var source = ParseSource(request.Source);
            var result = await service.AddBarcodeAsync(productId, request.Code ?? string.Empty, source, AppUser.Actor(user), cancellationToken);
            var body = new ProductBarcodeAddResponse(ProductBarcodeResponse.From(result.Barcode), result.Created);
            return result.Created
                ? Results.Created($"/api/barcodes/{result.Barcode.Code}", body)
                : Results.Ok(body);
        });

    private static Task<IResult> RemoveBarcodeAsync(
        Guid productId,
        string code,
        ClaimsPrincipal user,
        IProductMediaService service,
        CancellationToken cancellationToken) =>
        HandleAsync(async () =>
        {
            await service.RemoveBarcodeAsync(productId, code, AppUser.Actor(user), cancellationToken);
            return Results.NoContent();
        });

    private static Task<IResult> SetAvoidAsync(
        Guid productId,
        SetAvoidRequest request,
        ClaimsPrincipal user,
        IProductMediaService service,
        CancellationToken cancellationToken) =>
        HandleAsync(async () => Results.Ok(ProductMediaResponse.From(
            await service.SetAvoidAsync(productId, request.Avoid, request.Since, AppUser.Actor(user), cancellationToken))));

    private static Task<IResult> FindByBarcodeAsync(string code, IProductMediaService service, CancellationToken cancellationToken) =>
        HandleAsync(async () =>
        {
            var lookup = await service.FindByBarcodeAsync(code, cancellationToken);
            return Results.Ok(new BarcodeLookupResponse(
                lookup.Code,
                lookup.ProductIds,
                lookup.Remembered is null ? null : RememberedBarcodeResponse.From(lookup.Remembered)));
        });

    private static async Task<IResult> ListRememberedAsync(IProductMediaService service, CancellationToken cancellationToken) =>
        Results.Ok((await service.ListRememberedAsync(cancellationToken)).Select(RememberedBarcodeResponse.From).ToList());

    /// <summary>The body <c>{ off }</c> is optional, so it is read by hand: an empty PUT remembers the bare code.</summary>
    private static async Task<IResult> RememberAsync(
        string code,
        HttpRequest httpRequest,
        ClaimsPrincipal user,
        IProductMediaService service,
        CancellationToken cancellationToken)
    {
        RememberBarcodeRequest? request = null;
        if (httpRequest.HasJsonContentType())
        {
            try
            {
                request = await httpRequest.ReadFromJsonAsync<RememberBarcodeRequest>(cancellationToken);
            }
            catch (System.Text.Json.JsonException)
            {
                return Results.Problem(detail: "The body must be { \"off\": { name, brand, quantity, imageUrl } } or empty.", statusCode: StatusCodes.Status400BadRequest);
            }
        }

        return await HandleAsync(async () =>
        {
            var off = request?.Off is { } hint ? new OpenFoodFactsProduct(hint.Name, hint.Brand, hint.Quantity, hint.ImageUrl) : null;
            return Results.Ok(RememberedBarcodeResponse.From(await service.RememberAsync(code, off, AppUser.Actor(user), cancellationToken)));
        });
    }

    private static Task<IResult> ForgetAsync(string code, IProductMediaService service, CancellationToken cancellationToken) =>
        HandleAsync(async () =>
        {
            await service.ForgetAsync(code, cancellationToken);
            return Results.NoContent();
        });

    /// <summary>
    /// Only the barcode goes to Open Food Facts. «error» (timeout, provider
    /// down) is an answer, not a failure: the scan screen says the database is
    /// not answering and the person goes on without it.
    /// </summary>
    private static async Task<IResult> LookupOpenFoodFactsAsync(
        string code,
        IOpenFoodFactsClient client,
        CancellationToken cancellationToken)
    {
        if (Barcode.TryNormalize(code) is not { } normalized)
        {
            return Problem(StatusCodes.Status400BadRequest, "invalidBarcode", new InvalidBarcodeException().Message);
        }

        var lookup = await client.LookupAsync(normalized, cancellationToken);
        return Results.Ok(new OpenFoodFactsResponse(
            lookup.Status switch
            {
                OpenFoodFactsStatus.Found => "found",
                OpenFoodFactsStatus.NotFound => "notFound",
                _ => "error"
            },
            lookup.Product is null ? null : OpenFoodFactsHintResponse.From(lookup.Product)));
    }

    internal static string SourceToWire(ProductBarcodeSource source) => ProductMediaService.SourceText(source);

    private static ProductBarcodeSource ParseSource(string? value) => value?.Trim() switch
    {
        null or "" or "scan" => ProductBarcodeSource.Scan,
        "manual" => ProductBarcodeSource.Manual,
        "openFoodFacts" => ProductBarcodeSource.OpenFoodFacts,
        _ => throw new ArgumentException("source must be scan, manual or openFoodFacts.")
    };

    private static async Task<IResult> HandleAsync(Func<Task<IResult>> operation)
    {
        try
        {
            return await operation();
        }
        catch (ProductNotFoundException exception)
        {
            return Problem(StatusCodes.Status404NotFound, "productNotFound", exception.Message);
        }
        catch (ProductPhotoNotFoundException exception)
        {
            return Problem(StatusCodes.Status404NotFound, "photoNotFound", exception.Message);
        }
        catch (ProductBarcodeNotFoundException exception)
        {
            return Problem(StatusCodes.Status404NotFound, "barcodeNotFound", exception.Message);
        }
        catch (BarcodeTakenException exception)
        {
            return Results.Problem(
                detail: exception.Message,
                statusCode: StatusCodes.Status409Conflict,
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "barcodeTaken",
                    ["existingProductId"] = exception.ProductId
                });
        }
        catch (ProductMediaLimitException exception)
        {
            return Problem(StatusCodes.Status409Conflict, exception.Code, exception.Message);
        }
        catch (ProductMediaSaveException exception)
        {
            return Problem(StatusCodes.Status409Conflict, "changedMeanwhile", exception.Message);
        }
        catch (InvalidProductPhotoException exception)
        {
            return exception.Reason switch
            {
                InvalidProductPhotoReason.TooLarge => Problem(StatusCodes.Status413PayloadTooLarge, "photoTooLarge", exception.Message),
                InvalidProductPhotoReason.NotAnImage => Problem(StatusCodes.Status415UnsupportedMediaType, "photoNotAnImage", exception.Message),
                _ => Problem(StatusCodes.Status400BadRequest, "photoMissing", exception.Message)
            };
        }
        catch (InvalidBarcodeException exception)
        {
            return Problem(StatusCodes.Status400BadRequest, "invalidBarcode", exception.Message);
        }
        catch (ArgumentException exception)
        {
            return Results.Problem(detail: exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static IResult Problem(int statusCode, string code, string detail) =>
        Results.Problem(detail: detail, statusCode: statusCode, extensions: new Dictionary<string, object?> { ["code"] = code });
}

public sealed record AddBarcodeRequest(string? Code, string? Source);

/// <param name="Since">The original date of a mark moved from a phone; ignored when clearing.</param>
public sealed record SetAvoidRequest(bool Avoid, DateTimeOffset? Since = null);

public sealed record RememberBarcodeRequest(OpenFoodFactsHintResponse? Off);

public sealed record ProductPhotoResponse(
    Guid Id,
    DateTimeOffset CreatedAt,
    int Width,
    int Height,
    long Size,
    bool IsMain,
    string Url,
    string ThumbnailUrl)
{
    public static string UrlOf(Guid photoId) => $"/api/product-photos/{photoId:D}";

    public static ProductPhotoResponse From(ProductPhotoView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return new ProductPhotoResponse(
            view.Id, view.CreatedAt, view.Width, view.Height, view.SizeBytes, view.IsMain, UrlOf(view.Id), UrlOf(view.Id) + "/thumbnail");
    }
}

public sealed record ProductBarcodeResponse(string Code, string Source, DateTimeOffset CreatedAt)
{
    public static ProductBarcodeResponse From(ProductBarcodeView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return new ProductBarcodeResponse(view.Code, ProductMediaEndpoints.SourceToWire(view.Source), view.CreatedAt);
    }
}

public sealed record AvoidMarkResponse(DateTimeOffset Since);

public sealed record ProductMediaResponse(
    Guid ProductId,
    IReadOnlyList<ProductPhotoResponse> Photos,
    Guid? MainPhotoId,
    IReadOnlyList<ProductBarcodeResponse> Barcodes,
    AvoidMarkResponse? Avoid)
{
    public static ProductMediaResponse From(ProductMediaView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return new ProductMediaResponse(
            view.ProductId,
            view.Photos.Select(ProductPhotoResponse.From).ToList(),
            view.MainPhotoId,
            view.Barcodes.Select(ProductBarcodeResponse.From).ToList(),
            view.AvoidSince is { } since ? new AvoidMarkResponse(since) : null);
    }
}

public sealed record ProductPhotoUploadResponse(ProductPhotoResponse Photo, bool Created, bool FirstPhoto);

public sealed record ProductBarcodeAddResponse(ProductBarcodeResponse Barcode, bool Created);

public sealed record OpenFoodFactsHintResponse(string? Name, string? Brand, string? Quantity, string? ImageUrl)
{
    public static OpenFoodFactsHintResponse From(OpenFoodFactsProduct product)
    {
        ArgumentNullException.ThrowIfNull(product);
        return new OpenFoodFactsHintResponse(product.Name, product.Brand, product.Quantity, product.ImageUrl);
    }
}

public sealed record RememberedBarcodeResponse(string Code, DateTimeOffset RememberedAt, OpenFoodFactsHintResponse? Off)
{
    public static RememberedBarcodeResponse From(RememberedBarcodeView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return new RememberedBarcodeResponse(view.Code, view.RememberedAt, view.Off is null ? null : OpenFoodFactsHintResponse.From(view.Off));
    }
}

public sealed record BarcodeLookupResponse(string Code, IReadOnlyList<Guid> ProductIds, RememberedBarcodeResponse? Remembered);

/// <param name="Status">«found», «notFound» or «error» (the database did not answer).</param>
public sealed record OpenFoodFactsResponse(string Status, OpenFoodFactsHintResponse? Product);
