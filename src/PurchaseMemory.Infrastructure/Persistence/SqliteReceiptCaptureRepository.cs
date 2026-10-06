using Microsoft.EntityFrameworkCore;
using PurchaseMemory.Application.Receipts;
using PurchaseMemory.Domain.Receipts;

namespace PurchaseMemory.Infrastructure.Persistence;

public sealed class SqliteReceiptCaptureRepository(PurchaseMemoryDbContext dbContext)
    : IReceiptCaptureRepository
{
    public async Task<ReceiptCapture?> AddOrFindExactDuplicateAsync(
        ReceiptCapture receipt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        dbContext.ReceiptCaptures.Add(receipt);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (DbUpdateException)
        {
            // A concurrent uploader can win the unique image-set fingerprint
            // after our pre-check. Clear only this scoped unit of work, then
            // turn the database constraint into the same idempotent outcome.
            dbContext.ChangeTracker.Clear();
            var duplicate = await FindByImageSetSha256Async(receipt.ImageSha256, cancellationToken);
            if (duplicate is not null)
            {
                return duplicate;
            }

            throw;
        }
    }

    public Task<ReceiptCapture?> FindByIdAsync(Guid receiptId, CancellationToken cancellationToken)
    {
        return dbContext.ReceiptCaptures
            .Include(receipt => receipt.Items)
            .Include(receipt => receipt.ImageParts)
            .Include(receipt => receipt.ReviewAuditEntries)
            .SingleOrDefaultAsync(receipt => receipt.Id == receiptId, cancellationToken);
    }

    public Task<ReceiptCapture?> FindByImageSetSha256Async(
        string imageSetSha256,
        CancellationToken cancellationToken)
    {
        return dbContext.ReceiptCaptures
            .AsNoTracking()
            .Include(receipt => receipt.Items)
            .Include(receipt => receipt.ImageParts)
            .SingleOrDefaultAsync(receipt => receipt.ImageSha256 == imageSetSha256, cancellationToken);
    }

    public async Task<IReadOnlyList<ReceiptCapture>> ListRecentAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        return await dbContext.ReceiptCaptures
            .AsNoTracking()
            .Include(receipt => receipt.Items)
            .Include(receipt => receipt.ImageParts)
            .OrderByDescending(receipt => receipt.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ReceiptCapture>> ListPendingAsync(CancellationToken cancellationToken)
    {
        return await dbContext.ReceiptCaptures
            .Where(receipt => receipt.Status == ReceiptProcessingStatus.Queued
                || receipt.Status == ReceiptProcessingStatus.Processing)
            .OrderBy(receipt => receipt.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }
}
