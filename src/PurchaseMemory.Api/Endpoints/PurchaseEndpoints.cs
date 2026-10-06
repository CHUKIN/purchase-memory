using System.Security.Claims;
using PurchaseMemory.Api.Auth;
using PurchaseMemory.Application.Purchases;
using PurchaseMemory.Domain.Receipts;

namespace PurchaseMemory.Api.Endpoints;

public static class PurchaseEndpoints
{
    public static IEndpointRouteBuilder MapPurchaseEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/purchases", GetFeedAsync)
            .WithName("SearchPurchases");

        endpoints.MapGet("/api/purchases/{purchaseId:guid}", GetAsync)
            .WithName("GetPurchase");

        endpoints.MapPut("/api/purchases/{purchaseId:guid}", UpdateAsync)
            .WithName("UpdatePurchase");

        return endpoints;
    }

    private static async Task<IResult> GetFeedAsync(
        string? q,
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? status,
        Guid? categoryId,
        bool? uncategorized,
        string? brand,
        string[]? tagId,
        int? page,
        int? pageSize,
        IPurchaseDraftService service,
        CancellationToken cancellationToken)
    {
        if (!TryParseStatus(status, out var reviewStatus))
        {
            return Results.Problem(
                detail: "Unknown status filter. Use all, needsAttention or reviewed.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (!TagEndpoints.TryParseTagIds(tagId, out var tagIds))
        {
            return Results.Problem(detail: TagEndpoints.InvalidTagIdMessage, statusCode: StatusCodes.Status400BadRequest);
        }

        PurchaseFeedPage feed;
        try
        {
            feed = await service.GetFeedAsync(
                new PurchaseFeedQuery
                {
                    Query = q,
                    From = from,
                    To = to,
                    Status = reviewStatus,
                    CategoryId = categoryId,
                    Uncategorized = uncategorized ?? false,
                    Brand = brand,
                    TagIds = tagIds,
                    Page = page ?? 1,
                    PageSize = pageSize ?? PurchaseFeedQuery.DefaultPageSize
                },
                cancellationToken);
        }
        catch (PurchaseMemory.Application.Products.Categories.ProductCategoryNotFoundException exception)
        {
            // Same answer as the catalog: an unknown filter id is a bad request, not a missing page.
            return Results.Problem(detail: exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (PurchaseMemory.Application.Products.Tags.ProductTagNotFoundException exception)
        {
            return Results.Problem(detail: exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        return Results.Ok(PurchaseFeedResponse.FromPage(feed));
    }

    private static async Task<IResult> GetAsync(
        Guid purchaseId,
        IPurchaseEditService service,
        CancellationToken cancellationToken)
    {
        return Results.Ok(PurchaseDetailResponse.FromView(await service.GetAsync(purchaseId, cancellationToken)));
    }

    private static async Task<IResult> UpdateAsync(
        Guid purchaseId,
        UpdatePurchaseRequest request,
        ClaimsPrincipal user,
        IPurchaseEditService service,
        CancellationToken cancellationToken)
    {
        var command = request.ToCommand(purchaseId) with { Actor = AppUser.Actor(user) };
        return Results.Ok(PurchaseDetailResponse.FromView(await service.UpdateAsync(command, cancellationToken)));
    }

    private static bool TryParseStatus(string? status, out PurchaseReviewStatus reviewStatus)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            reviewStatus = PurchaseReviewStatus.All;
            return true;
        }

        return Enum.TryParse(status, ignoreCase: true, out reviewStatus)
            && Enum.IsDefined(reviewStatus);
    }
}

public sealed record PurchaseItemResponse(
    string Name,
    decimal? Quantity,
    string? Unit,
    decimal? UnitPrice,
    decimal? LineTotal,
    string Kind,
    int? DiscountedItemPosition,
    bool? MatchesFilter = null);

public sealed record PurchaseFeedResponse(
    IReadOnlyList<PurchaseResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    bool HasMore,
    bool Truncated)
{
    public static PurchaseFeedResponse FromPage(PurchaseFeedPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new PurchaseFeedResponse(
            page.Items.Select(PurchaseResponse.FromView).ToList(),
            page.Page,
            page.PageSize,
            page.TotalCount,
            page.HasMore,
            page.Truncated);
    }
}

public sealed record PurchaseResponse(
    Guid Id,
    string? MerchantName,
    DateTimeOffset? PurchasedAt,
    string? Currency,
    decimal? ReceiptTotal,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<PurchaseItemResponse> Items,
    IReadOnlyList<PurchaseDraftIssueResponse> Issues)
{
    public static PurchaseResponse FromView(PurchaseDraftView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        return new PurchaseResponse(
            view.PurchaseId,
            view.MerchantName,
            view.PurchasedAt,
            view.Currency,
            view.ReceiptTotal,
            view.CreatedAt,
            view.UpdatedAt,
            view.Items.Select(item => new PurchaseItemResponse(
                item.Name,
                item.Quantity,
                item.Unit,
                item.UnitPrice,
                item.LineTotal,
                ReceiptLineKindWire.ToWire(item.Kind),
                item.DiscountedItemPosition,
                item.MatchesFilter)).ToList(),
            view.Issues.Select(PurchaseDraftIssueResponse.FromIssue).ToList());
    }
}

public sealed record PurchaseDraftIssueResponse(
    string Type,
    int? ItemPosition,
    string? ItemName,
    string RecognizedValue,
    string SuggestedFix,
    BoundingBoxPayload? BoundingBox)
{
    public static PurchaseDraftIssueResponse FromIssue(PurchaseDraftIssue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);

        return new PurchaseDraftIssueResponse(
            MapType(issue.Type),
            issue.ItemPosition,
            issue.ItemName,
            issue.RecognizedValue,
            issue.SuggestedFix,
            BoundingBoxPayload.FromDomain(issue.BoundingBox));
    }

    private static string MapType(PurchaseDraftIssueType type) => type switch
    {
        PurchaseDraftIssueType.ReceiptTotalMismatch => "receiptTotalMismatch",
        PurchaseDraftIssueType.MissingItemPrice => "missingItemPrice",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };
}

/// <param name="ProductLink">
/// "auto" (the matcher decides), "existing" (link to <paramref name="ProductId"/>)
/// or "new" (create a product from the line name). Omitted means "existing"
/// when a product id is sent and "auto" otherwise.
/// </param>
public sealed record UpdatePurchaseItemRequest(
    string? Name,
    decimal? Quantity,
    string? Unit,
    decimal? UnitPrice,
    decimal? LineTotal,
    string? Kind = null,
    string? ProductLink = null,
    Guid? ProductId = null,
    BoundingBoxPayload? BoundingBox = null);

/// <summary>
/// A complete corrected snapshot of a saved purchase: lines left out are
/// removed, new lines are added, exactly like the review of a new receipt.
/// </summary>
public sealed record UpdatePurchaseRequest(
    DateTimeOffset? ExpectedUpdatedAt,
    string? MerchantName,
    DateTimeOffset? PurchasedAt,
    string? Currency,
    decimal? ReceiptTotal,
    IReadOnlyList<UpdatePurchaseItemRequest>? Items)
{
    public EditPurchaseCommand ToCommand(Guid purchaseId)
    {
        if (ExpectedUpdatedAt is null)
        {
            throw new ArgumentException("expectedUpdatedAt is required: send the updatedAt value the purchase was opened with.");
        }

        if (Items is null || Items.Count == 0)
        {
            throw new ArgumentException("A purchase must contain at least one line.");
        }

        return new EditPurchaseCommand(
            purchaseId,
            ExpectedUpdatedAt,
            MerchantName,
            PurchasedAt,
            Currency,
            ReceiptTotal,
            Items.Select((item, index) => ToItemCommand(item, index + 1)).ToList());
    }

    private static EditPurchaseItemCommand ToItemCommand(UpdatePurchaseItemRequest item, int line)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.Name))
        {
            throw new ArgumentException($"Line {line}: the name is required.");
        }

        ReceiptLineKind? kind;
        try
        {
            kind = ReceiptLineKindWire.FromWire(item.Kind);
        }
        catch (ArgumentException exception)
        {
            throw new ArgumentException($"Line {line}: {exception.Message}");
        }

        var link = item.ProductLink?.Trim().ToLowerInvariant() switch
        {
            null or "" => item.ProductId is null ? ProductLinkChoice.Automatic : ProductLinkChoice.Existing,
            "auto" => ProductLinkChoice.Automatic,
            "existing" => ProductLinkChoice.Existing,
            "new" => ProductLinkChoice.CreateNew,
            _ => throw new ArgumentException($"Line {line}: unknown productLink '{item.ProductLink}'. Use 'auto', 'existing' or 'new'.")
        };

        return new EditPurchaseItemCommand(
            item.Name,
            item.Quantity,
            item.Unit,
            item.UnitPrice,
            item.LineTotal,
            kind,
            link,
            link == ProductLinkChoice.Existing ? item.ProductId : null,
            item.BoundingBox?.ToDomain());
    }
}

public sealed record PurchaseProductCandidateResponse(Guid ProductId, string Name, double Score);

public sealed record PurchaseDetailItemResponse(
    int Position,
    string Name,
    decimal? Quantity,
    string? Unit,
    decimal? UnitPrice,
    decimal? LineTotal,
    string Kind,
    int? DiscountedItemPosition,
    Guid? ProductId,
    string? ProductName,
    bool PendingMatch,
    IReadOnlyList<PurchaseProductCandidateResponse> Candidates,
    BoundingBoxPayload? BoundingBox,
    Guid Id);

public sealed record PurchaseDetailResponse(
    Guid Id,
    Guid? SourceReceiptId,
    string? MerchantName,
    DateTimeOffset? PurchasedAt,
    string? Currency,
    decimal? ReceiptTotal,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<PurchaseDetailItemResponse> Items,
    IReadOnlyList<PurchaseDraftIssueResponse> Issues)
{
    public static PurchaseDetailResponse FromView(PurchaseDetailView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        return new PurchaseDetailResponse(
            view.PurchaseId,
            view.SourceReceiptId,
            view.MerchantName,
            view.PurchasedAt,
            view.Currency,
            view.ReceiptTotal,
            view.CreatedAt,
            view.UpdatedAt,
            view.Items.Select(item => new PurchaseDetailItemResponse(
                item.Position,
                item.Name,
                item.Quantity,
                item.Unit,
                item.UnitPrice,
                item.LineTotal,
                ReceiptLineKindWire.ToWire(item.Kind),
                item.DiscountedItemPosition,
                item.ProductId,
                item.ProductName,
                item.PendingMatch,
                item.Candidates.Select(candidate => new PurchaseProductCandidateResponse(
                    candidate.ProductId, candidate.Name, candidate.Score)).ToList(),
                BoundingBoxPayload.FromDomain(item.BoundingBox),
                item.Id)).ToList(),
            view.Issues.Select(PurchaseDraftIssueResponse.FromIssue).ToList());
    }
}
