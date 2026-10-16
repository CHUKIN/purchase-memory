using System.Security.Claims;
using PurchaseMemory.Api.Auth;
using PurchaseMemory.Application.Products.Categories;
using PurchaseMemory.Domain.Products;

namespace PurchaseMemory.Api.Endpoints;

/// <summary>
/// The category tree, a product's category and brand, the AI backfill and
/// the product audit trail (which also records tag changes).
/// Everything here sits behind the approved-user fallback policy.
/// </summary>
public static class CategoryEndpoints
{
    public static IEndpointRouteBuilder MapCategoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/categories", ListAsync).WithName("ListCategories");
        endpoints.MapPost("/api/categories", CreateAsync).WithName("CreateCategory");
        endpoints.MapPut("/api/categories/{categoryId:guid}", RenameAsync).WithName("RenameCategory");
        endpoints.MapDelete("/api/categories/{categoryId:guid}", DeleteAsync).WithName("DeleteCategory");
        endpoints.MapPut("/api/products/{productId:guid}/classification", ClassifyAsync).WithName("ClassifyProduct");
        endpoints.MapGet("/api/products/{productId:guid}/audit", AuditAsync).WithName("GetProductAuditTrail");
        endpoints.MapPost("/api/products/categorization/backfill", BackfillAsync)
            .WithName("BackfillProductCategories")
            .RequireRateLimiting(ReceiptUploadRateLimiting.BackfillPolicyName);
        return endpoints;
    }

    private static async Task<IResult> ListAsync(IProductCategoryService service, CancellationToken cancellationToken)
    {
        var tree = await service.GetTreeAsync(cancellationToken);
        return Results.Ok(tree.Select(ToResponse).ToList());
    }

    private static Task<IResult> CreateAsync(
        CreateCategoryRequest request,
        IProductCategoryService service,
        CancellationToken cancellationToken) =>
        HandleAsync(async () => Results.Ok(ToResponse(
            await service.CreateAsync(request.Name ?? string.Empty, request.ParentId, cancellationToken))));

    private static Task<IResult> RenameAsync(
        Guid categoryId,
        RenameCategoryRequest request,
        IProductCategoryService service,
        CancellationToken cancellationToken) =>
        HandleAsync(async () => Results.Ok(ToResponse(
            await service.RenameAsync(categoryId, request.Name ?? string.Empty, cancellationToken))));

    private static Task<IResult> DeleteAsync(
        Guid categoryId,
        ClaimsPrincipal user,
        IProductCategoryService service,
        CancellationToken cancellationToken) =>
        HandleAsync(async () =>
        {
            var deletion = await service.DeleteAsync(categoryId, AppUser.Actor(user), cancellationToken);
            return Results.Ok(new CategoryDeletionResponse(
                deletion.CategoryId,
                deletion.MovedToCategoryId,
                deletion.MovedProductCount));
        });

    private static Task<IResult> ClassifyAsync(
        Guid productId,
        ClassifyProductRequest request,
        ClaimsPrincipal user,
        IProductCategoryService service,
        CancellationToken cancellationToken) =>
        HandleAsync(async () =>
        {
            var classification = await service.ClassifyProductAsync(
                new ClassifyProductCommand(productId, request.SetCategory, request.CategoryId, request.SetBrand, request.Brand),
                AppUser.Actor(user),
                cancellationToken);
            return Results.Ok(new ProductClassificationResponse(
                classification.ProductId,
                classification.CategoryId,
                SourceToWire(classification.CategorySource),
                classification.Brand,
                SourceToWire(classification.BrandSource)));
        });

    private static async Task<IResult> AuditAsync(
        Guid productId,
        IProductCategoryService service,
        CancellationToken cancellationToken)
    {
        var entries = await service.GetAuditTrailAsync(productId, cancellationToken);
        return Results.Ok(entries.Select(entry => new ProductAuditEntryResponse(
            entry.Actor,
            entry.ActorEmail,
            entry.FieldName,
            entry.Source switch
            {
                ProductAuditChangeSource.Human => "human",
                ProductAuditChangeSource.Suggestion => "suggestion",
                ProductAuditChangeSource.TagDeleted => "tagDeleted",
                ProductAuditChangeSource.Matching => "matching",
                _ => "categoryDeleted"
            },
            entry.OldValue,
            entry.NewValue,
            entry.OccurredAt)).ToList());
    }

    /// <summary>
    /// Suggests categories for up to <paramref name="limit"/> products nobody has
    /// classified. This is the one endpoint that spends money on the model, so
    /// its limit is small by default and every call is logged with token usage.
    /// </summary>
    private static Task<IResult> BackfillAsync(
        int? limit,
        bool? retryAttempted,
        CategorizationBackfillGate gate,
        IProductCategorizationService service,
        CancellationToken cancellationToken) =>
        HandleAsync(async () =>
        {
            // One run at a time: two overlapping runs would pay twice for the same products.
            using var lease = gate.TryEnter();
            if (lease is null)
            {
                return Results.Problem(
                    detail: "A categorization run is already in progress.",
                    statusCode: StatusCodes.Status409Conflict);
            }

            var summary = await service.SuggestForUnclassifiedAsync(
                limit ?? 100,
                retryAttempted ?? false,
                cancellationToken);
            return Results.Ok(new CategorizationRunResponse(
                summary.Configured,
                summary.Considered,
                summary.Suggested,
                summary.FailedBatches,
                summary.InputTokens,
                summary.OutputTokens));
        });

    private static async Task<IResult> HandleAsync(Func<Task<IResult>> operation)
    {
        try
        {
            return await operation();
        }
        catch (ProductCategoryNotFoundException exception)
        {
            return Results.Problem(detail: exception.Message, statusCode: StatusCodes.Status404NotFound);
        }
        catch (ProductNotFoundException exception)
        {
            return Results.Problem(detail: exception.Message, statusCode: StatusCodes.Status404NotFound);
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

    public static string? SourceToWire(ProductAttributeSource? source) => source switch
    {
        ProductAttributeSource.Suggested => "suggested",
        ProductAttributeSource.Confirmed => "confirmed",
        _ => null
    };

    private static CategoryResponse ToResponse(ProductCategoryView view) =>
        new(view.Id, view.ParentId, view.Name, view.SystemKey, view.Position, view.ProductCount);
}

public sealed record CreateCategoryRequest(string? Name, Guid? ParentId);

public sealed record RenameCategoryRequest(string? Name);

public sealed record ClassifyProductRequest(bool SetCategory, Guid? CategoryId, bool SetBrand, string? Brand);

public sealed record CategoryResponse(
    Guid Id,
    Guid? ParentId,
    string Name,
    string? SystemKey,
    int Position,
    int ProductCount);

public sealed record CategoryDeletionResponse(Guid CategoryId, Guid? MovedToCategoryId, int MovedProductCount);

public sealed record ProductClassificationResponse(
    Guid ProductId,
    Guid? CategoryId,
    string? CategorySource,
    string? Brand,
    string? BrandSource);

public sealed record ProductAuditEntryResponse(
    string Actor,
    string? ActorEmail,
    string FieldName,
    string Source,
    string? OldValue,
    string? NewValue,
    DateTimeOffset OccurredAt);

public sealed record CategorizationRunResponse(
    bool Configured,
    int Considered,
    int Suggested,
    int FailedBatches,
    int InputTokens,
    int OutputTokens);

/// <summary>Process-wide single-flight guard for the paid backfill.</summary>
public sealed class CategorizationBackfillGate
{
    private int _running;

    /// <summary>A lease to dispose when the run ends, or <c>null</c> when one is already running.</summary>
    public IDisposable? TryEnter() => Interlocked.CompareExchange(ref _running, 1, 0) == 0 ? new Lease(this) : null;

    private sealed class Lease(CategorizationBackfillGate gate) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                Volatile.Write(ref gate._running, 0);
            }
        }
    }
}
