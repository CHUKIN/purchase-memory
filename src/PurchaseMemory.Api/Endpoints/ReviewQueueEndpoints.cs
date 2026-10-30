using System.Security.Claims;
using System.Text.Json;
using PurchaseMemory.Api.Auth;
using PurchaseMemory.Application.ReviewQueue;

namespace PurchaseMemory.Api.Endpoints;

/// <summary>
/// «Ревью»: what in the stored data still needs a person, plus purchases with
/// unchecked lines and the caller's own lines to rate. Read-only; fixes go
/// through the existing receipt, purchase, product and impression endpoints.
/// Behind the approved-user fallback policy like every other data endpoint;
/// the rating part is always the caller's (from the token), the rest is the
/// household's shared data, as everywhere else.
/// The list takes the feed's classification filters — <c>categoryId</c> (with
/// its subcategories) or <c>uncategorized=true</c>, and a repeatable
/// <c>tagId</c> (all tags required); the count stays the whole queue, because
/// it feeds the navigation badge, which knows nothing about the screen's filter.
/// </summary>
public static class ReviewQueueEndpoints
{
    public static IEndpointRouteBuilder MapReviewQueueEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/review-queue", GetAsync).WithName("GetReviewQueue");
        endpoints.MapGet("/api/review-queue/count", CountAsync).WithName("CountReviewQueue");
        return endpoints;
    }

    private static async Task<IResult> GetAsync(
        int? limitPerKind,
        Guid? categoryId,
        bool? uncategorized,
        string[]? tagId,
        ClaimsPrincipal user,
        IReviewQueueService service,
        CancellationToken cancellationToken)
    {
        var limit = limitPerKind ?? ReviewQueueService.DefaultLimitPerKind;
        if (limit is < 1 or > ReviewQueueService.MaximumLimitPerKind)
        {
            return Results.Problem(
                detail: $"limitPerKind must be between 1 and {ReviewQueueService.MaximumLimitPerKind}.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (!TagEndpoints.TryParseTagIds(tagId, out var tagIds))
        {
            return Results.Problem(detail: TagEndpoints.InvalidTagIdMessage, statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            var queue = await service.GetHubAsync(
                limit,
                new ReviewQueueFilter(categoryId, uncategorized ?? false, tagIds),
                AppUser.Actor(user).UserId,
                cancellationToken);
            return Results.Ok(ReviewQueueResponse.From(queue));
        }
        catch (PurchaseMemory.Application.Products.Categories.ProductCategoryNotFoundException exception)
        {
            return Results.Problem(detail: exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (PurchaseMemory.Application.Products.Tags.ProductTagNotFoundException exception)
        {
            // Same answer as the feed and catalog: an unknown filter id is a bad request.
            return Results.Problem(detail: exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (ArgumentException exception)
        {
            return Results.Problem(detail: exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<IResult> CountAsync(ClaimsPrincipal user, IReviewQueueService service, CancellationToken cancellationToken)
    {
        var queue = await service.GetHubAsync(0, ReviewQueueFilter.None, AppUser.Actor(user).UserId, cancellationToken);
        return Results.Ok(new ReviewQueueCountResponse(
            queue.TotalCount,
            queue.Groups.ToDictionary(group => KindToWire(group.Kind), group => group.TotalCount),
            ReviewQueueSummaryResponse.From(queue.Summary)));
    }

    public static string KindToWire(ReviewQueueItemKind kind) => JsonNamingPolicy.CamelCase.ConvertName(kind.ToString());

    public static string SectionToWire(ReviewQueueSection section) => JsonNamingPolicy.CamelCase.ConvertName(section.ToString());
}

public sealed record ReviewQueueCountResponse(int TotalCount, IReadOnlyDictionary<string, int> ByKind, ReviewQueueSummaryResponse? Summary);

public sealed record ReviewQueueSectionCountResponse(string Section, int Count);

/// <summary>The whole hub (never filtered): the Home card, the tab badge and the hub read the same numbers.</summary>
public sealed record ReviewQueueSummaryResponse(int TotalCount, IReadOnlyList<ReviewQueueSectionCountResponse> Sections)
{
    public static ReviewQueueSummaryResponse? From(ReviewQueueSummary? summary) => summary is null
        ? null
        : new(
            summary.TotalCount,
            summary.Sections.Select(section => new ReviewQueueSectionCountResponse(
                ReviewQueueEndpoints.SectionToWire(section.Section), section.Count)).ToList());
}

public sealed record ReviewQueueItemResponse(
    string Kind,
    string Id,
    string Title,
    DateTimeOffset OccurredAt,
    Guid? ReceiptId,
    Guid? PurchaseId,
    Guid? ProductId,
    Guid? CategoryId,
    string? CategoryName,
    string? Currency,
    decimal? ReceiptTotal,
    decimal? LinesTotal,
    int? AffectedLineCount,
    int? ItemPosition,
    // Flagged product only (empty otherwise): what it might really be, best first.
    IReadOnlyList<ReviewQueueCandidateResponse> Candidates,
    // Flagged product only: distinct purchases it appears on.
    int? PurchaseCount,
    // Flagged product only: it has no category either; the same item asks for one.
    bool NeedsCategory,
    // Unchecked purchase only: all its lines; affectedLineCount is the unchecked ones.
    int? LineCount,
    // Unrated line only: the line to rate, its store and price.
    Guid? PurchaseItemId,
    string? MerchantName,
    decimal? Price,
    // Product kinds: the human names (null: show title, the receipt text).
    string? NamePl = null,
    string? NameRu = null,
    // Suggested names only: which of the two still wait for «Верно».
    bool NamePlSuggested = false,
    bool NameRuSuggested = false);

public sealed record ReviewQueueCandidateResponse(
    Guid ProductId,
    string Name,
    string? Brand,
    Guid? CategoryId,
    int PurchaseCount,
    double Score,
    string? NamePl = null,
    string? NameRu = null);

public sealed record ReviewQueueGroupResponse(string Kind, int TotalCount, IReadOnlyList<ReviewQueueItemResponse> Items);

public sealed record ReviewQueueResponse(
    int TotalCount,
    IReadOnlyList<ReviewQueueGroupResponse> Groups,
    // The hub only (null from the MCP tool): the whole queue by section, whatever the filter.
    ReviewQueueSummaryResponse? Summary)
{
    public static ReviewQueueResponse From(ReviewQueueResult result) => new(
        result.TotalCount,
        result.Groups
            .Select(group => new ReviewQueueGroupResponse(
                ReviewQueueEndpoints.KindToWire(group.Kind),
                group.TotalCount,
                group.Items
                    .Select(item => new ReviewQueueItemResponse(
                        ReviewQueueEndpoints.KindToWire(item.Kind),
                        item.Id,
                        item.Title,
                        item.OccurredAt,
                        item.ReceiptId,
                        item.PurchaseId,
                        item.ProductId,
                        item.CategoryId,
                        item.CategoryName,
                        item.Currency,
                        item.ReceiptTotal,
                        item.LinesTotal,
                        item.AffectedLineCount,
                        item.ItemPosition,
                        (item.Candidates ?? [])
                            .Select(candidate => new ReviewQueueCandidateResponse(
                                candidate.ProductId,
                                candidate.Name,
                                candidate.Brand,
                                candidate.CategoryId,
                                candidate.PurchaseCount,
                                candidate.Score,
                                candidate.NamePl,
                                candidate.NameRu))
                            .ToList(),
                        item.PurchaseCount,
                        item.NeedsCategory,
                        item.LineCount,
                        item.PurchaseItemId,
                        item.MerchantName,
                        item.Price,
                        item.NamePl,
                        item.NameRu,
                        item.NamePlSuggested,
                        item.NameRuSuggested))
                    .ToList()))
            .ToList(),
        ReviewQueueSummaryResponse.From(result.Summary));
}
