using Microsoft.EntityFrameworkCore;
using PurchaseMemory.Application.Products.Tags;
using PurchaseMemory.Domain.Products;

namespace PurchaseMemory.Infrastructure.Persistence;

public sealed class ProductTagRepository : IProductTagRepository
{
    private readonly PurchaseMemoryDbContext _dbContext;

    public ProductTagRepository(PurchaseMemoryDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<ProductTag>> ListTagsAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.ProductTags.ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, int>> CountProductsByTagAsync(CancellationToken cancellationToken)
    {
        var rows = await _dbContext.ProductTagLinks
            .AsNoTracking()
            .GroupBy(link => link.TagId)
            .Select(group => new { TagId = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(row => row.TagId, row => row.Count);
    }

    public void AddTag(ProductTag tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        _dbContext.ProductTags.Add(tag);
    }

    public void RemoveTag(ProductTag tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        _dbContext.ProductTags.Remove(tag);
    }

    public async Task<IReadOnlyList<ProductTagLink>> ListLinksForTagAsync(Guid tagId, CancellationToken cancellationToken)
    {
        return await _dbContext.ProductTagLinks
            .Where(link => link.TagId == tagId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProductTagLink>> ListLinksForProductAsync(
        Guid productId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.ProductTagLinks
            .Where(link => link.ProductId == productId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<ProductTagSummary>>> ListTagsForProductsAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(productIds);
        if (productIds.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<ProductTagSummary>>();
        }

        var identifiers = productIds.Distinct().ToList();
        var rows = await _dbContext.ProductTagLinks
            .AsNoTracking()
            .Where(link => identifiers.Contains(link.ProductId))
            .Join(
                _dbContext.ProductTags.AsNoTracking(),
                link => link.TagId,
                tag => tag.Id,
                (link, tag) => new { link.ProductId, tag.Id, tag.Name, tag.NormalizedName })
            .ToListAsync(cancellationToken);

        // Ordered here, in .NET, like the tag list, so both providers agree.
        return rows
            .GroupBy(row => row.ProductId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ProductTagSummary>)group
                    .OrderBy(row => row.NormalizedName, StringComparer.Ordinal)
                    .ThenBy(row => row.Name, StringComparer.Ordinal)
                    .Select(row => new ProductTagSummary(row.Id, row.Name))
                    .ToList());
    }

    public Task<bool> ProductExistsAsync(Guid productId, CancellationToken cancellationToken)
    {
        return _dbContext.Products.AnyAsync(product => product.Id == productId, cancellationToken);
    }

    public void AddLink(ProductTagLink link)
    {
        ArgumentNullException.ThrowIfNull(link);
        _dbContext.ProductTagLinks.Add(link);
    }

    public void RemoveLink(ProductTagLink link)
    {
        ArgumentNullException.ThrowIfNull(link);
        _dbContext.ProductTagLinks.Remove(link);
    }

    public void AddAuditEntry(ProductAuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _dbContext.ProductAuditEntries.Add(entry);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            // The service checks names and references first; reaching here means
            // another request changed the same tags in between (unique name,
            // duplicate link, or a link to a tag deleted meanwhile).
            throw new ProductTagConflictException(
                "The tags were changed at the same time. Reload and try again.",
                exception);
        }
    }
}
