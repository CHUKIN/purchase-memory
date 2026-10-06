using PurchaseMemory.Application.Receipts;
using PurchaseMemory.Domain.Products;

namespace PurchaseMemory.Application.Products.Tags;

/// <summary>
/// Household tags and which products carry them. Every change here is a
/// person's decision: nothing creates or assigns tags automatically.
/// </summary>
public interface IProductTagService
{
    /// <summary>All tags, alphabetically, with how many products carry each.</summary>
    Task<IReadOnlyList<ProductTagView>> ListAsync(CancellationToken cancellationToken);

    Task<ProductTagView> CreateAsync(string name, CancellationToken cancellationToken);

    Task<ProductTagView> RenameAsync(Guid tagId, string name, CancellationToken cancellationToken);

    /// <summary>Deletes the tag and its links; the products themselves stay.</summary>
    Task<ProductTagDeletion> DeleteAsync(Guid tagId, ReceiptActor actor, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the product's tags with exactly <paramref name="tagIds"/>
    /// (duplicates ignored). Returns the product's tags afterwards.
    /// </summary>
    Task<IReadOnlyList<ProductTagSummary>> SetProductTagsAsync(
        Guid productId,
        IReadOnlyCollection<Guid> tagIds,
        ReceiptActor actor,
        CancellationToken cancellationToken);
}

/// <summary>Persistence boundary for tags and product↔tag links.</summary>
public interface IProductTagRepository
{
    /// <summary>Every tag, tracked, so callers may rename or delete them.</summary>
    Task<IReadOnlyList<ProductTag>> ListTagsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, int>> CountProductsByTagAsync(CancellationToken cancellationToken);

    void AddTag(ProductTag tag);

    void RemoveTag(ProductTag tag);

    /// <summary>Tracked links of one tag.</summary>
    Task<IReadOnlyList<ProductTagLink>> ListLinksForTagAsync(Guid tagId, CancellationToken cancellationToken);

    /// <summary>Tracked links of one product.</summary>
    Task<IReadOnlyList<ProductTagLink>> ListLinksForProductAsync(Guid productId, CancellationToken cancellationToken);

    /// <summary>Tags of each given product, alphabetical; products without tags are absent.</summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<ProductTagSummary>>> ListTagsForProductsAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken);

    Task<bool> ProductExistsAsync(Guid productId, CancellationToken cancellationToken);

    void AddLink(ProductTagLink link);

    void RemoveLink(ProductTagLink link);

    void AddAuditEntry(ProductAuditEntry entry);

    /// <summary>
    /// Saves; a concurrent change that breaks a uniqueness or reference rule
    /// (same name created twice, a link to a tag deleted meanwhile) surfaces as
    /// <see cref="ProductTagConflictException"/>.
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed record ProductTagSummary(Guid Id, string Name);

public sealed record ProductTagView(Guid Id, string Name, int ProductCount);

public sealed record ProductTagDeletion(Guid TagId, int UnlinkedProductCount);

public sealed class ProductTagNotFoundException(Guid tagId)
    : Exception($"Tag {tagId} was not found.")
{
    public Guid TagId { get; } = tagId;
}

/// <summary>The request conflicts with existing tags (duplicate name, concurrent change).</summary>
public sealed class ProductTagConflictException(string message, Exception? innerException = null)
    : Exception(message, innerException);
