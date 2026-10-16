using Microsoft.EntityFrameworkCore;
using PurchaseMemory.Application.Purchases;
using PurchaseMemory.Domain.Purchases;

namespace PurchaseMemory.Infrastructure.Persistence;

public sealed class SqlitePurchaseDraftRepository : IPurchaseDraftRepository
{
    private const int MaximumCandidatesForPrototypeSearch = 500;

    private readonly PurchaseMemoryDbContext _dbContext;

    public SqlitePurchaseDraftRepository(PurchaseMemoryDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public void Add(PurchaseDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        _dbContext.PurchaseDrafts.Add(draft);
    }

    public Task<PurchaseDraft?> FindByIdAsync(Guid purchaseId, CancellationToken cancellationToken)
    {
        return _dbContext.PurchaseDrafts
            .Include(draft => draft.Items)
            .SingleOrDefaultAsync(draft => draft.Id == purchaseId, cancellationToken);
    }

    public Task<PurchaseDraft?> FindBySourceReceiptIdAsync(
        Guid sourceReceiptId,
        CancellationToken cancellationToken)
    {
        return _dbContext.PurchaseDrafts
            .AsNoTracking()
            .Include(draft => draft.Items)
            .SingleOrDefaultAsync(draft => draft.SourceReceiptId == sourceReceiptId, cancellationToken);
    }

    public async Task<IReadOnlyList<PurchaseDraft>> FindPossibleDuplicateCandidatesAsync(
        DateTimeOffset? purchasedAt,
        decimal? receiptTotal,
        int maximumResults,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumResults);

        return await _dbContext.PurchaseDrafts
            .AsNoTracking()
            .Include(draft => draft.Items)
            .Where(draft => draft.PurchasedAt == purchasedAt && draft.ReceiptTotal == receiptTotal)
            .OrderByDescending(draft => draft.CreatedAt)
            .Take(maximumResults)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PurchaseDraft>> SearchAsync(
        string? query,
        int maximumResults,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumResults);

        var candidates = await _dbContext.PurchaseDrafts
            .AsNoTracking()
            .Include(draft => draft.Items)
            .OrderByDescending(draft => draft.CreatedAt)
            .Take(MaximumCandidatesForPrototypeSearch)
            .ToListAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(query))
        {
            return candidates.Take(maximumResults).ToList();
        }

        // SQLite's built-in case folding is ASCII-centric. The spike searches a
        // bounded in-memory candidate set so Russian and Polish names behave as
        // users expect. PostgreSQL will replace this implementation later.
        return candidates
            .Where(draft => Contains(draft.MerchantName, query)
                || draft.Items.Any(item => Contains(item.Name, query)))
            .Take(maximumResults)
            .ToList();
    }

    public async Task<IReadOnlyList<PurchaseDraft>> ListFeedCandidatesAsync(
        DateTimeOffset? fromInclusive,
        DateTimeOffset? toInclusive,
        int maximumCandidates,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCandidates);

        var candidates = _dbContext.PurchaseDrafts
            .AsNoTracking()
            .Include(draft => draft.Items)
            .AsQueryable();

        if (fromInclusive is not null)
        {
            candidates = candidates.Where(draft => draft.PurchasedAt >= fromInclusive);
        }

        if (toInclusive is not null)
        {
            candidates = candidates.Where(draft => draft.PurchasedAt <= toInclusive);
        }

        return await candidates
            // A purchase without a recognized date still belongs in the feed at
            // the moment it was saved, so CreatedAt is the ordering fallback.
            .OrderByDescending(draft => draft.PurchasedAt ?? draft.CreatedAt)
            .ThenByDescending(draft => draft.CreatedAt)
            .ThenBy(draft => draft.Id)
            .Take(maximumCandidates)
            .ToListAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        SaveChangesAsync(new Dictionary<Guid, Guid>(), cancellationToken);

    public async Task SaveChangesAsync(IReadOnlyDictionary<Guid, Guid> continuedLines, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(continuedLines);

        // A correction replaces the line rows; impressions follow their line.
        var attachments = await ProductImpressionRepository.DetachFromReplacedLinesAsync(_dbContext, continuedLines, cancellationToken);
        if (attachments.Count == 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        // Swap the line rows first, then point the impressions at the new rows
        // (see DetachFromReplacedLinesAsync for why one save cannot do both).
        // One transaction, so nobody ever sees an impression without its line.
        await using var transaction = _dbContext.Database.CurrentTransaction is null
            ? await _dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
        await _dbContext.SaveChangesAsync(cancellationToken);
        foreach (var (impression, lineId) in attachments)
        {
            impression.MoveToItem(lineId);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
    }

    private static bool Contains(string? source, string query)
    {
        return source?.Contains(query, StringComparison.OrdinalIgnoreCase) is true;
    }
}
