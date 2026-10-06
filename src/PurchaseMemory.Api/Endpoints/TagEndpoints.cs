using System.Security.Claims;
using PurchaseMemory.Api.Auth;
using PurchaseMemory.Application.Products.Categories;
using PurchaseMemory.Application.Products.Tags;

namespace PurchaseMemory.Api.Endpoints;

/// <summary>
/// Household tags and a product's tags. Everything here sits behind the
/// approved-user fallback policy, like the category endpoints.
/// </summary>
public static class TagEndpoints
{
    public static IEndpointRouteBuilder MapTagEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/tags", ListAsync).WithName("ListTags");
        endpoints.MapPost("/api/tags", CreateAsync).WithName("CreateTag");
        endpoints.MapPut("/api/tags/{tagId:guid}", RenameAsync).WithName("RenameTag");
        endpoints.MapDelete("/api/tags/{tagId:guid}", DeleteAsync).WithName("DeleteTag");
        endpoints.MapPut("/api/products/{productId:guid}/tags", SetProductTagsAsync).WithName("SetProductTags");
        return endpoints;
    }

    private static async Task<IResult> ListAsync(IProductTagService service, CancellationToken cancellationToken)
    {
        var tags = await service.ListAsync(cancellationToken);
        return Results.Ok(tags.Select(ToResponse).ToList());
    }

    private static Task<IResult> CreateAsync(
        TagNameRequest request,
        IProductTagService service,
        CancellationToken cancellationToken) =>
        HandleAsync(async () => Results.Ok(ToResponse(
            await service.CreateAsync(request.Name ?? string.Empty, cancellationToken))));

    private static Task<IResult> RenameAsync(
        Guid tagId,
        TagNameRequest request,
        IProductTagService service,
        CancellationToken cancellationToken) =>
        HandleAsync(async () => Results.Ok(ToResponse(
            await service.RenameAsync(tagId, request.Name ?? string.Empty, cancellationToken))));

    private static Task<IResult> DeleteAsync(
        Guid tagId,
        ClaimsPrincipal user,
        IProductTagService service,
        CancellationToken cancellationToken) =>
        HandleAsync(async () =>
        {
            var deletion = await service.DeleteAsync(tagId, AppUser.Actor(user), cancellationToken);
            return Results.Ok(new TagDeletionResponse(deletion.TagId, deletion.UnlinkedProductCount));
        });

    private static async Task<IResult> SetProductTagsAsync(
        Guid productId,
        SetProductTagsRequest request,
        ClaimsPrincipal user,
        IProductTagService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var tags = await service.SetProductTagsAsync(
                productId,
                request.TagIds ?? [],
                AppUser.Actor(user),
                cancellationToken);
            return Results.Ok(new ProductTagsResponse(productId, tags.Select(ToResponse).ToList()));
        }
        catch (ProductTagNotFoundException exception)
        {
            // An unknown id in the body is a bad request; only the product in the path can be «not found».
            return Results.Problem(detail: exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (ProductNotFoundException exception)
        {
            return Results.Problem(detail: exception.Message, statusCode: StatusCodes.Status404NotFound);
        }
        catch (ProductTagConflictException exception)
        {
            return Results.Problem(detail: exception.Message, statusCode: StatusCodes.Status409Conflict);
        }
        catch (ArgumentException exception)
        {
            return Results.Problem(detail: exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<IResult> HandleAsync(Func<Task<IResult>> operation)
    {
        try
        {
            return await operation();
        }
        catch (ProductTagNotFoundException exception)
        {
            return Results.Problem(detail: exception.Message, statusCode: StatusCodes.Status404NotFound);
        }
        catch (ProductTagConflictException exception)
        {
            return Results.Problem(detail: exception.Message, statusCode: StatusCodes.Status409Conflict);
        }
        catch (ArgumentException exception)
        {
            return Results.Problem(detail: exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    public const string InvalidTagIdMessage = "tagId must be a tag identifier (GUID).";

    /// <summary>
    /// Reads repeated <c>tagId</c> query values. An empty value (<c>?tagId=</c>)
    /// means «no tag filter», like an empty category; anything else must be a GUID.
    /// </summary>
    public static bool TryParseTagIds(string[]? values, out IReadOnlyCollection<Guid>? tagIds)
    {
        var parsed = new List<Guid>();
        foreach (var value in values ?? [])
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (!Guid.TryParse(value, out var id))
            {
                tagIds = null;
                return false;
            }

            parsed.Add(id);
        }

        tagIds = parsed.Count == 0 ? null : parsed;
        return true;
    }

    public static TagResponse ToResponse(ProductTagSummary tag) => new(tag.Id, tag.Name);

    private static TagSummaryResponse ToResponse(ProductTagView view) => new(view.Id, view.Name, view.ProductCount);
}

public sealed record TagNameRequest(string? Name);

public sealed record SetProductTagsRequest(IReadOnlyList<Guid>? TagIds);

/// <summary>A tag as it appears on a product.</summary>
public sealed record TagResponse(Guid Id, string Name);

/// <summary>A tag in the household list, with how many products carry it.</summary>
public sealed record TagSummaryResponse(Guid Id, string Name, int ProductCount);

public sealed record TagDeletionResponse(Guid TagId, int UnlinkedProductCount);

public sealed record ProductTagsResponse(Guid ProductId, IReadOnlyList<TagResponse> Tags);
