using PurchaseMemory.Application.Receipts;
using PurchaseMemory.Domain.Products;

namespace PurchaseMemory.Application.Products.Categories;

public interface IProductCategoryService
{
    Task<IReadOnlyList<ProductCategoryView>> GetTreeAsync(CancellationToken cancellationToken);

    Task<ProductCategoryView> CreateAsync(string name, Guid? parentId, CancellationToken cancellationToken);

    Task<ProductCategoryView> RenameAsync(Guid categoryId, string name, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes a category. Its products move to the parent category, or become
    /// uncategorized for a top-level one. A top-level category that still has
    /// subcategories is refused, so a single click never reshuffles a whole
    /// branch.
    /// </summary>
    Task<ProductCategoryDeletion> DeleteAsync(Guid categoryId, ReceiptActor actor, CancellationToken cancellationToken);

    /// <summary>A person's decision; it is authoritative and marks the values confirmed.</summary>
    Task<ProductClassificationView> ClassifyProductAsync(
        ClassifyProductCommand command,
        ReceiptActor actor,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ProductAuditEntryView>> GetAuditTrailAsync(Guid productId, CancellationToken cancellationToken);
}

/// <summary>
/// Only the flagged parts change: confirming a suggested category must not wipe
/// a brand the request did not mention.
/// </summary>
public sealed record ClassifyProductCommand(
    Guid ProductId,
    bool SetCategory,
    Guid? CategoryId,
    bool SetBrand,
    string? Brand);

public sealed record ProductCategoryView(
    Guid Id,
    Guid? ParentId,
    string Name,
    string? SystemKey,
    int Position,
    int ProductCount);

public sealed record ProductCategoryDeletion(Guid CategoryId, Guid? MovedToCategoryId, int MovedProductCount);

public sealed record ProductClassificationView(
    Guid ProductId,
    Guid? CategoryId,
    ProductAttributeSource? CategorySource,
    string? Brand,
    ProductAttributeSource? BrandSource);

public sealed record ProductAuditEntryView(
    string Actor,
    string? ActorEmail,
    string FieldName,
    ProductAuditChangeSource Source,
    string? OldValue,
    string? NewValue,
    DateTimeOffset OccurredAt);

public sealed class ProductCategoryNotFoundException(Guid categoryId)
    : Exception($"Category {categoryId} was not found.")
{
    public Guid CategoryId { get; } = categoryId;
}

public sealed class ProductNotFoundException(Guid productId)
    : Exception($"Product {productId} was not found.")
{
    public Guid ProductId { get; } = productId;
}

/// <summary>The request conflicts with the tree as it is (duplicate name, non-empty branch, depth).</summary>
public sealed class ProductCategoryConflictException(string message, Exception? innerException = null)
    : Exception(message, innerException);
