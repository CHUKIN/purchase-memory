using System.Text.Json;
using PurchaseMemory.Application.ReviewQueue;

namespace PurchaseMemory.Api.Endpoints;

/// <summary>
/// «Нужно проверить»: what in the stored data still needs a person. Read-only;
/// fixes go through the existing receipt and product endpoints. Behind the
/// approved-user fallback policy like every other data endpoint.
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
            var queue = await service.GetAsync(
                limit,
                new ReviewQueueFilter(categoryId, uncategorized ?? false, tagIds),
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

    private static async Task<IResult> CountAsync(IReviewQueueService service, CancellationToken cancellationToken)
    {
        var queue = await service.GetAsync(0, cancellationToken);
        return Results.Ok(new ReviewQueueCountResponse(
            queue.TotalCount,
            queue.Groups.ToDictionary(group => KindToWire(group.Kind), group => group.TotalCount)));
    }

    public static string KindToWire(ReviewQueueItemKind kind) => JsonNamingPolicy.CamelCase.ConvertName(kind.ToString());
}

public sealed record ReviewQueueCountResponse(int TotalCount, IReadOnlyDictionary<string, int> ByKind);

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
    bool NeedsCategory);

public sealed record ReviewQueueCandidateResponse(
    Guid ProductId,
    string Name,
    string? Brand,
    Guid? CategoryId,
    int PurchaseCount,
    double Score);

public sealed record ReviewQueueGroupResponse(string Kind, int TotalCount, IReadOnlyList<ReviewQueueItemResponse> Items);

public sealed record ReviewQueueResponse(int TotalCount, IReadOnlyList<ReviewQueueGroupResponse> Groups)
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
                                candidate.Score))
                            .ToList(),
                        item.PurchaseCount,
                        item.NeedsCategory))
                    .ToList()))
            .ToList());
}
