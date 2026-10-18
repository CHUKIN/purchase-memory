using Microsoft.EntityFrameworkCore;
using PurchaseMemory.Application.Receipts.TextLocation;
using PurchaseMemory.Domain.Receipts;

namespace PurchaseMemory.Infrastructure.Persistence;

/// <summary>
/// Text layouts and the background step's work queue. It shares the scoped
/// <see cref="PurchaseMemoryDbContext"/> with the receipt repository, so
/// <see cref="ReplaceAsync"/> is written by the same SaveChanges that records
/// the receipt's new state.
/// </summary>
public sealed class ReceiptTextLayoutRepository(PurchaseMemoryDbContext dbContext) : IReceiptTextLayoutRepository
{
    public Task<ReceiptTextLayout?> FindAsync(Guid receiptId, CancellationToken cancellationToken) =>
        dbContext.ReceiptTextLayouts
            .AsNoTracking()
            .SingleOrDefaultAsync(layout => layout.ReceiptCaptureId == receiptId, cancellationToken);

    public async Task ReplaceAsync(ReceiptTextLayout layout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var existing = await dbContext.ReceiptTextLayouts
            .SingleOrDefaultAsync(entry => entry.ReceiptCaptureId == layout.ReceiptCaptureId, cancellationToken);
        if (existing is null)
        {
            dbContext.ReceiptTextLayouts.Add(layout);
        }
        else
        {
            existing.ReplaceWith(layout);
        }
    }

    public async Task<ReceiptTextLocationCandidate?> FindNextCandidateAsync(
        DateTimeOffset retryBefore,
        DateTimeOffset? createdSince,
        int maximumAttempts,
        CancellationToken cancellationToken)
    {
        var query = dbContext.ReceiptCaptures
            .AsNoTracking()
            .Where(receipt => receipt.Status == ReceiptProcessingStatus.ReadyForReview
                || receipt.Status == ReceiptProcessingStatus.Saved)
            .Where(receipt => receipt.TextLocationStatus == ReceiptTextLocationStatus.NotRequested
                || (receipt.TextLocationStatus == ReceiptTextLocationStatus.Failed
                    && receipt.TextLocationAttemptedAt < retryBefore
                    && receipt.TextLocationAttempts < maximumAttempts));
        if (createdSince is { } since)
        {
            query = query.Where(receipt => receipt.CreatedAt >= since);
        }

        var receipt = await query
            .Include(receipt => receipt.ImageParts)
            .OrderByDescending(receipt => receipt.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        return receipt is null
            ? null
            : new ReceiptTextLocationCandidate(
                receipt.Id,
                receipt.CreatedAt,
                receipt.ImageParts.OrderBy(part => part.Position).ToList());
    }

    public async Task<int> CountCallsSinceAsync(DateTimeOffset since, CancellationToken cancellationToken) =>
        await dbContext.ReceiptCaptures
            .Where(receipt => receipt.TextLocationAttemptedAt >= since)
            .SumAsync(receipt => receipt.TextLocationCalls, cancellationToken);

    public void DiscardPendingChanges() => dbContext.ChangeTracker.Clear();
}
