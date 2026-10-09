using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using PurchaseMemory.Api.Auth;
using PurchaseMemory.Application.Receipts;
using PurchaseMemory.Domain.Receipts;
using PurchaseMemory.Infrastructure.Recognition;

namespace PurchaseMemory.Api.Endpoints;

public static class ReceiptEndpoints
{
    public static IEndpointRouteBuilder MapReceiptEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/recognition/status", GetRecognitionStatusAsync)
            .WithName("GetRecognitionStatus");

        endpoints.MapPost("/api/receipts", UploadAsync)
            .DisableAntiforgery()
            .WithMetadata(
                new RequestSizeLimitAttribute(ReceiptCaptureLimits.MaximumMultipartRequestSizeBytes),
                new RequestFormLimitsAttribute
                {
                    MultipartBodyLengthLimit = ReceiptCaptureLimits.MaximumMultipartRequestSizeBytes
                })
            .RequireRateLimiting(ReceiptUploadRateLimiting.PolicyName)
            .WithName("UploadReceipt");

        endpoints.MapGet("/api/receipts", ListRecentAsync)
            .WithName("ListRecentReceipts");

        // Re-queueing runs recognition again, which costs as much as an upload.
        endpoints.MapPost("/api/receipts/{receiptId:guid}/process", QueueAsync)
            .RequireRateLimiting(ReceiptUploadRateLimiting.PolicyName)
            .WithName("ProcessReceipt");

        endpoints.MapGet("/api/receipts/{receiptId:guid}", GetAsync)
            .WithName("GetReceipt");

        endpoints.MapGet("/api/receipts/{receiptId:guid}/image", GetImageAsync)
            .WithName("GetReceiptImage");

        endpoints.MapGet("/api/receipts/{receiptId:guid}/images/{position:int}", GetImagePartAsync)
            .WithName("GetReceiptImagePart");

        endpoints.MapPut("/api/receipts/{receiptId:guid}/review", ReviewAsync)
            .WithName("ReviewReceipt");

        endpoints.MapGet("/api/receipts/{receiptId:guid}/audit", GetAuditTrailAsync)
            .WithName("GetReceiptAuditTrail");

        endpoints.MapPost("/api/receipts/{receiptId:guid}/save", SaveAsync)
            .WithName("SaveReceipt");

        return endpoints;
    }

    private static async Task<IResult> GetRecognitionStatusAsync(
        IReceiptRecognitionStatusProvider provider,
        CancellationToken cancellationToken)
    {
        return Results.Ok(await provider.GetStatusAsync(cancellationToken));
    }

    // `process=true` queues recognition in the same request, so one upload
    // costs one rate-limit token instead of two (upload + /process). The web
    // capture screen always uses it; batch capture relies on it to fit a whole
    // burst of separate receipts.
    private static async Task<IResult> UploadAsync(
        HttpRequest request,
        ClaimsPrincipal user,
        IReceiptProcessingService service,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken,
        [FromQuery] bool process = false)
    {
        var images = (await request.ReadFormAsync(cancellationToken)).Files;
        if (images.Count is < 1 or > ReceiptCaptureLimits.MaximumPartCount)
        {
            throw new InvalidDataException(
                $"A receipt must contain between 1 and {ReceiptCaptureLimits.MaximumPartCount} image parts.");
        }


        if (images.Any(image => image.Length == 0))
        {
            throw new InvalidDataException("Receipt image is empty.");
        }


        if (images.Any(image => image.Length > ReceiptCaptureLimits.MaximumPartSizeBytes))
        {
            throw new InvalidDataException(
                $"Each receipt image must be at most {ReceiptCaptureLimits.MaximumPartSizeBytes / 1024 / 1024} MB.");
        }


        if (images.Sum(image => image.Length) > ReceiptCaptureLimits.MaximumTotalSizeBytes)
        {
            throw new InvalidDataException(
                $"Receipt images must be at most {ReceiptCaptureLimits.MaximumTotalSizeBytes / 1024 / 1024} MB in total.");
        }

        var streams = images.Select(image => image.OpenReadStream()).ToList();
        try
        {
            var result = await service.UploadAsync(
                new UploadReceiptCommand(images.Select((image, index) =>
                    new ReceiptImageUpload(streams[index], image.ContentType)).ToList(),
                    AppUser.Actor(user)),
                cancellationToken);

            var receipt = result.Receipt;
            if (process && !result.IsExactDuplicate)
            {
                try
                {
                    receipt = await service.QueueAsync(receipt.ReceiptId, cancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // The upload is already durable; the receipt page offers an
                    // explicit queue action for a receipt left in Uploaded.
                    ReceiptEndpointLogMessages.UploadedButNotQueued(
                        loggerFactory.CreateLogger(typeof(ReceiptEndpoints)),
                        exception,
                        receipt.ReceiptId);
                }
            }

            var response = new ReceiptUploadResponse(
                ReceiptResponse.FromView(receipt),
                result.IsExactDuplicate);
            return result.IsExactDuplicate
                ? Results.Ok(response)
                : Results.Created($"/api/receipts/{receipt.ReceiptId}", response);
        }
        finally
        {
            foreach (var stream in streams)
            {
                await stream.DisposeAsync();
            }
        }
    }

    private static async Task<IResult> ListRecentAsync(
        [FromQuery] int limit,
        IReceiptProcessingService service,
        CancellationToken cancellationToken)
    {
        var effectiveLimit = limit == 0 ? 20 : limit;
        var receipts = await service.ListRecentAsync(effectiveLimit, cancellationToken);
        return Results.Ok(receipts.Select(ReceiptResponse.FromView));
    }

    private static async Task<IResult> QueueAsync(
        Guid receiptId,
        IReceiptProcessingService service,
        CancellationToken cancellationToken)
    {
        var view = await service.QueueAsync(receiptId, cancellationToken);
        return Results.Accepted($"/api/receipts/{receiptId}", ReceiptResponse.FromView(view));
    }

    private static async Task<IResult> GetAsync(
        Guid receiptId,
        IReceiptProcessingService service,
        CancellationToken cancellationToken)
    {
        return Results.Ok(ReceiptResponse.FromView(await service.GetAsync(receiptId, cancellationToken)));
    }

    private static async Task<IResult> GetImageAsync(
        Guid receiptId,
        bool? original,
        IReceiptProcessingService service,
        IReceiptImageStore imageStore,
        CancellationToken cancellationToken)
    {
        var receipt = await service.GetAsync(receiptId, cancellationToken);
        var part = receipt.ImageParts.OrderBy(part => part.Position).First();
        return await StreamImageAsync(imageStore, part.StorageKey, part.ContentType, original, cancellationToken);
    }

    private static async Task<IResult> GetImagePartAsync(
        Guid receiptId,
        int position,
        bool? original,
        IReceiptProcessingService service,
        IReceiptImageStore imageStore,
        CancellationToken cancellationToken)
    {
        var receipt = await service.GetAsync(receiptId, cancellationToken);
        var part = receipt.ImageParts.SingleOrDefault(part => part.Position == position);
        if (part is null)
        {
            return Results.NotFound();
        }

        return await StreamImageAsync(imageStore, part.StorageKey, part.ContentType, original, cancellationToken);
    }

    /// <summary>
    /// The app shows the compressed display copy; <c>?original=true</c> returns
    /// the upload byte for byte (with its metadata) for download or diagnostics.
    /// </summary>
    private static async Task<IResult> StreamImageAsync(
        IReceiptImageStore imageStore,
        string storageKey,
        string originalContentType,
        bool? original,
        CancellationToken cancellationToken)
    {
        var purpose = original is true ? ReceiptImagePurpose.Original : ReceiptImagePurpose.Viewing;
        var image = await imageStore.OpenReadAsync(storageKey, purpose, cancellationToken);
        return Results.Stream(
            image.Content,
            image.ContentType ?? originalContentType,
            enableRangeProcessing: image.Content.CanSeek);
    }

    private static async Task<IResult> ReviewAsync(
        Guid receiptId,
        ReviewReceiptRequest request,
        ClaimsPrincipal user,
        IReceiptProcessingService service,
        CancellationToken cancellationToken)
    {
        var command = request.ToCommand(receiptId) with { Actor = AppUser.Actor(user) };
        var view = await service.ReviewAsync(command, cancellationToken);

        return Results.Ok(ReceiptResponse.FromView(view));
    }

    private static async Task<IResult> GetAuditTrailAsync(
        Guid receiptId,
        IReceiptProcessingService service,
        CancellationToken cancellationToken)
    {
        var entries = await service.GetAuditTrailAsync(receiptId, cancellationToken);
        return Results.Ok(entries.Select(ReceiptAuditEntryResponse.FromView));
    }

    private static async Task<IResult> SaveAsync(
        Guid receiptId,
        IReceiptProcessingService service,
        CancellationToken cancellationToken)
    {
        var purchase = await service.SaveAsync(new SaveReceiptCommand(receiptId), cancellationToken);
        return Results.Ok(PurchaseResponse.FromView(purchase));
    }
}

/// <summary>
/// A photo region in relative fractions of the image size. Client-supplied
/// values pass the same validation as model output, so an unusable box simply
/// disappears instead of reaching the UI.
/// </summary>
public sealed record BoundingBoxPayload(double X, double Y, double Width, double Height)
{
    public static BoundingBoxPayload? FromDomain(BoundingBox? box) =>
        box is null ? null : new BoundingBoxPayload(box.X, box.Y, box.Width, box.Height);

    public BoundingBox? ToDomain() => BoundingBox.TryCreate(X, Y, Width, Height);
}

/// <param name="Kind">
/// "product" or "discount". Optional on input: without it a negative amount
/// means a discount. <paramref name="DiscountedItemPosition"/> is output only;
/// the backend always derives it from line order and ignores the client value.
/// </param>
public sealed record ReviewReceiptItemRequest(
    string Name,
    decimal? Quantity,
    string? Unit,
    decimal? UnitPrice,
    decimal? LineTotal,
    BoundingBoxPayload? BoundingBox = null,
    string? Kind = null,
    int? DiscountedItemPosition = null);

/// <summary>
/// Line kinds cross the REST wire as the same lowercase strings the PWA types
/// declare, mapped by hand like the receipt status.
/// </summary>
internal static partial class ReceiptEndpointLogMessages
{
    [LoggerMessage(EventId = 7101, Level = LogLevel.Warning,
        Message = "Receipt {ReceiptId} was uploaded but could not be queued for recognition")]
    public static partial void UploadedButNotQueued(ILogger logger, Exception exception, Guid receiptId);
}

public static class ReceiptLineKindWire
{
    public const string Product = "product";
    public const string Discount = "discount";

    public static string ToWire(ReceiptLineKind kind) => kind switch
    {
        ReceiptLineKind.Product => Product,
        ReceiptLineKind.Discount => Discount,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown receipt line kind.")
    };

    public static ReceiptLineKind? FromWire(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "" => null,
        Product => ReceiptLineKind.Product,
        Discount => ReceiptLineKind.Discount,
        _ => throw new ArgumentException($"Unknown item kind '{value}'. Use '{Product}' or '{Discount}'.", nameof(value))
    };
}

public sealed record ReviewReceiptRequest(
    string? MerchantName,
    DateTimeOffset? PurchasedAt,
    string? Currency,
    decimal? ReceiptTotal,
    IReadOnlyList<ReviewReceiptItemRequest> Items,
    BoundingBoxPayload? ReceiptTotalBoundingBox = null)
{
    /// <summary>
    /// Client input becomes an application command here, so every bounding box
    /// the PWA echoes back is revalidated before it can reach the database.
    /// </summary>
    public ReviewReceiptCommand ToCommand(Guid receiptId)
    {
        return new ReviewReceiptCommand(
            receiptId,
            MerchantName,
            PurchasedAt,
            Currency,
            ReceiptTotal,
            Items.Select(item => new ReceiptItemCommand(
                item.Name,
                item.Quantity,
                item.Unit,
                item.UnitPrice,
                item.LineTotal,
                item.BoundingBox?.ToDomain(),
                ReceiptLineKindWire.FromWire(item.Kind))).ToList(),
            ReceiptTotalBoundingBox?.ToDomain());
    }
}

public sealed record ReceiptSnapshotResponse(
    string? MerchantName,
    DateTimeOffset? PurchasedAt,
    string? Currency,
    decimal? ReceiptTotal,
    IReadOnlyList<ReviewReceiptItemRequest> Items,
    BoundingBoxPayload? ReceiptTotalBoundingBox);

public sealed record ReceiptUploadResponse(
    ReceiptResponse Receipt,
    bool ExactDuplicate);

public sealed record ReceiptImagePartResponse(
    int Position,
    string ContentType,
    long SizeBytes,
    string SourceImageUrl);

public sealed record ReceiptResponse(
    Guid Id,
    string Status,
    DateTimeOffset UploadedAt,
    DateTimeOffset UpdatedAt,
    string OriginalFileName,
    string SourceImageUrl,
    IReadOnlyList<ReceiptImagePartResponse> ImageParts,
    string? Provider,
    string? Model,
    long? ProcessingDurationMs,
    ReceiptSnapshotResponse? Snapshot,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<PurchaseDraftIssueResponse> Issues,
    string? Error,
    Guid? SavedPurchaseId)
{
    public static ReceiptResponse FromView(ReceiptCaptureView view)
    {
        var snapshot = view.Status is ReceiptProcessingStatus.ReadyForReview or ReceiptProcessingStatus.Saved
            ? new ReceiptSnapshotResponse(
                view.MerchantName,
                view.PurchasedAt,
                view.Currency,
                view.ReceiptTotal,
                view.Items.Select(item => new ReviewReceiptItemRequest(
                    item.Name,
                    item.Quantity,
                    item.Unit,
                    item.UnitPrice,
                    item.LineTotal,
                    BoundingBoxPayload.FromDomain(item.BoundingBox),
                    ReceiptLineKindWire.ToWire(item.Kind),
                    item.DiscountedItemPosition)).ToList(),
                BoundingBoxPayload.FromDomain(view.ReceiptTotalBoundingBox))
            : null;

        return new ReceiptResponse(
            view.ReceiptId,
            MapStatus(view.Status),
            view.CreatedAt,
            view.UpdatedAt,
            $"receipt{ExtensionFor(view.ImageContentType)}",
            $"/api/receipts/{view.ReceiptId}/image",
            view.ImageParts.Select(part => new ReceiptImagePartResponse(
                part.Position,
                part.ContentType,
                part.SizeBytes,
                $"/api/receipts/{view.ReceiptId}/images/{part.Position}")).ToList(),
            view.ExtractorName,
            view.ExtractorModel,
            null,
            snapshot,
            view.Warnings,
            view.Issues.Select(PurchaseDraftIssueResponse.FromIssue).ToList(),
            view.ErrorMessage,
            view.SavedPurchaseId);
    }

    private static string MapStatus(ReceiptProcessingStatus status) => status switch
    {
        ReceiptProcessingStatus.Uploaded => "uploaded",
        ReceiptProcessingStatus.Queued => "queued",
        ReceiptProcessingStatus.Processing => "processing",
        ReceiptProcessingStatus.ReadyForReview => "ready",
        ReceiptProcessingStatus.Failed => "failed",
        ReceiptProcessingStatus.Saved => "saved",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };

    private static string ExtensionFor(string contentType) => contentType switch
    {
        "image/png" => ".png",
        "image/webp" => ".webp",
        _ => ".jpg"
    };
}

public sealed record ReceiptAuditEntryResponse(
    DateTimeOffset OccurredAt,
    string Actor,
    string? ActorEmail,
    string FieldName,
    string ChangeType,
    string? OldValue,
    string? NewValue)
{
    public static ReceiptAuditEntryResponse FromView(ReceiptReviewAuditEntryView view) =>
        new(
            view.OccurredAt,
            view.Actor,
            view.ActorEmail,
            view.FieldName,
            view.ChangeType.ToString(),
            view.OldValue,
            view.NewValue);
}
