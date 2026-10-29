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

    /// <summary>
    /// A person typed (or cleared) the product's human name in one language;
    /// it becomes confirmed, so the AI never replaces it. An empty name means
    /// «show the receipt text».
    /// </summary>
    /// <exception cref="ProductNotFoundException">No such product.</exception>
    /// <exception cref="ArgumentException">The name is too long.</exception>
    Task<ProductNamesView> SetProductNameAsync(
        Guid productId,
        ProductNameLanguage language,
        string? name,
        ReceiptActor actor,
        CancellationToken cancellationToken);

    /// <summary>
    /// «Верно»: confirms every suggested name as it is. Nothing suggested —
    /// nothing changes. With <paramref name="shown"/> (the names the person
    /// looked at) it confirms only if the product still has exactly those, so
    /// a name re-suggested meanwhile is never confirmed unseen.
    /// </summary>
    /// <exception cref="ProductNotFoundException">No such product.</exception>
    /// <exception cref="ProductCategoryConflictException">The names changed since they were shown.</exception>
    Task<ProductNamesView> ConfirmSuggestedNamesAsync(
        Guid productId,
        ShownProductNames? shown,
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

/// <summary>The names a person saw when pressing «Верно»; <c>null</c> is «no name shown».</summary>
public sealed record ShownProductNames(string? NamePl, string? NameRu);

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
