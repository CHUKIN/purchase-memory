using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using PurchaseMemory.Application.Products;
using PurchaseMemory.Domain.Impressions;
using PurchaseMemory.Domain.Products;
using PurchaseMemory.Domain.Purchases;

namespace PurchaseMemory.Infrastructure.Persistence;

/// <summary>
/// Writes behind product create, rename, merge and «это другой товар». The
/// merge repoints rows with set-based updates (provider-neutral
/// <c>ExecuteUpdate</c>/<c>ExecuteDelete</c>) inside the caller's transaction,
/// so a product with years of history is merged in a handful of statements.
/// </summary>
public sealed class ProductMaintenanceRepository(PurchaseMemoryDbContext dbContext) : IProductMaintenanceRepository
{
    public Task<Product?> FindProductForUpdateAsync(Guid productId, CancellationToken cancellationToken)
    {
        return dbContext.Products.FirstOrDefaultAsync(product => product.Id == productId, cancellationToken);
    }

    public Task<Product?> FindByNormalizedNameAsync(string normalizedName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(normalizedName);
        return dbContext.Products
            .AsNoTracking()
            .OrderBy(product => product.CreatedAt)
            .FirstOrDefaultAsync(product => product.NormalizedName == normalizedName, cancellationToken);
    }

    public void AddProduct(Product product)
    {
        ArgumentNullException.ThrowIfNull(product);
        dbContext.Products.Add(product);
    }

    public void RemoveProduct(Product product)
    {
        ArgumentNullException.ThrowIfNull(product);
        dbContext.Products.Remove(product);
    }

    public void AddAuditEntry(ProductAuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        dbContext.ProductAuditEntries.Add(entry);
    }

    public async Task<IReadOnlyList<ProductAlias>> ListAliasesForUpdateAsync(
        Guid productId,
        CancellationToken cancellationToken)
    {
        return await dbContext.ProductAliases
            .Where(alias => alias.ProductId == productId)
            .ToListAsync(cancellationToken);
    }

    public Task RemoveReviewCandidatesAsync(Guid productId, CancellationToken cancellationToken)
    {
        return dbContext.ProductReviewCandidates
            .Where(candidate => candidate.ProductId == productId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<ProductReferenceMove> MoveReferencesAsync(
        Guid sourceId,
        Guid targetId,
        CancellationToken cancellationToken)
    {
        var lines = await dbContext.Set<PurchaseItem>()
            .Where(item => item.ProductId == sourceId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ProductId, targetId), cancellationToken);

        var impressions = await dbContext.Set<ProductImpression>()
            .Where(impression => impression.ProductId == sourceId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(impression => impression.ProductId, targetId), cancellationToken);

        // The alias key is store + wording, without the product, so the
        // source's aliases can never collide with the target's: each wording
        // already points at exactly one product.
        var aliases = await dbContext.ProductAliases
            .Where(alias => alias.ProductId == sourceId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(alias => alias.ProductId, targetId)
                    .SetProperty(alias => alias.Source, ProductAliasSource.HumanConfirmed),
                cancellationToken);

        // History is kept, under the product it now belongs to; the merge
        // entry the service adds explains where the older entries came from.
        var auditEntries = await dbContext.ProductAuditEntries
            .Where(entry => entry.ProductId == sourceId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(entry => entry.ProductId, targetId), cancellationToken);

        // Other flagged products that listed the source now list the target,
        // unless the target is that flagged product itself or already listed.
        await dbContext.ProductReviewCandidates
            .Where(candidate => candidate.CandidateProductId == sourceId
                && (candidate.ProductId == targetId
                    || dbContext.ProductReviewCandidates.Any(other =>
                        other.ProductId == candidate.ProductId && other.CandidateProductId == targetId)))
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.ProductReviewCandidates
            .Where(candidate => candidate.CandidateProductId == sourceId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.CandidateProductId, targetId), cancellationToken);

        // The source's own candidate list and tag links would cascade with it;
        // deleting them here keeps the outcome independent of tracked state.
        await dbContext.ProductReviewCandidates
            .Where(candidate => candidate.ProductId == sourceId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.ProductTagLinks
            .Where(link => link.ProductId == sourceId)
            .ExecuteDeleteAsync(cancellationToken);

        return new ProductReferenceMove(lines, aliases, impressions, auditEntries);
    }

    public async Task InTransactionAsync(Func<Task> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        try
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            await work();
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception) when (IsConcurrentChange(exception))
        {
            // The transaction was rolled back on dispose. Another request was
            // changing the same products: a deadlock between two crossing
            // merges, a row deleted in between, a broken reference.
            throw Conflict(exception);
        }
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (IsConcurrentChange(exception))
        {
            throw Conflict(exception);
        }
    }

    /// <summary>
    /// A database refusal, as opposed to a bug: EF's save failures, a provider
    /// error from the set-based steps, and Npgsql's «transient failure»
    /// wrapper around a deadlock (40P01) or serialization failure. The wrapper
    /// nests the cause at any depth (seen: InvalidOperationException →
    /// DbUpdateException → PostgresException), so the whole chain is checked.
    /// </summary>
    internal static bool IsConcurrentChange(Exception exception)
    {
        if (exception is ProductConflictException)
        {
            return false;
        }

        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbUpdateException or DbException)
            {
                return true;
            }
        }

        return false;
    }

    private static ProductConflictException Conflict(Exception exception) => new(
        "The products were changed at the same time. Reload and try again.",
        innerException: exception);
}
