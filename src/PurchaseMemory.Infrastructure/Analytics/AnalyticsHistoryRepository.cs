using Microsoft.EntityFrameworkCore;
using PurchaseMemory.Application.Analytics;
using PurchaseMemory.Domain.Analytics;
using PurchaseMemory.Infrastructure.Persistence;

namespace PurchaseMemory.Infrastructure.Analytics;

/// <summary>Provider-neutral EF Core access to the analytics question history.</summary>
public sealed class AnalyticsHistoryRepository : IAnalyticsHistoryRepository
{
    private readonly PurchaseMemoryDbContext _dbContext;

    public AnalyticsHistoryRepository(PurchaseMemoryDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<AnalyticsQuestionEntry?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        _dbContext.AnalyticsQuestions.SingleOrDefaultAsync(entry => entry.Id == id, cancellationToken);

    public Task<AnalyticsQuestionEntry?> FindLatestAsync(
        string normalizedQuestion,
        bool isFollowUp,
        string? planJson,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.AnalyticsQuestions
            .Where(entry => entry.NormalizedQuestion == normalizedQuestion && entry.IsFollowUp == isFollowUp);
        if (planJson is not null)
        {
            query = query.Where(entry => entry.PlanJson == planJson);
        }

        return query
            .OrderByDescending(entry => entry.AnsweredAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<(IReadOnlyList<AnalyticsHistoryRow> Rows, bool HasMore)> ListAsync(
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        var rows = await _dbContext.AnalyticsQuestions
            .AsNoTracking()
            .OrderByDescending(entry => entry.AnsweredAt)
            .ThenByDescending(entry => entry.CreatedAt)
            .Skip(offset)
            .Take(limit + 1)
            .Select(entry => new AnalyticsHistoryRow(
                entry.Id,
                entry.Question,
                entry.IsFollowUp,
                entry.Conclusion,
                entry.AskedByUserId,
                entry.AskedByEmail,
                entry.CreatedAt,
                entry.AnsweredAt))
            .ToListAsync(cancellationToken);
        var hasMore = rows.Count > limit;
        return (hasMore ? rows.Take(limit).ToList() : rows, hasMore);
    }

    public void Add(AnalyticsQuestionEntry entry) => _dbContext.AnalyticsQuestions.Add(entry);

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        await _dbContext.AnalyticsQuestions.Where(entry => entry.Id == id).ExecuteDeleteAsync(cancellationToken) > 0;

    public async Task TrimAsync(int keep, CancellationToken cancellationToken)
    {
        var stale = await _dbContext.AnalyticsQuestions
            .AsNoTracking()
            .OrderByDescending(entry => entry.AnsweredAt)
            .ThenByDescending(entry => entry.CreatedAt)
            .Skip(keep)
            .Select(entry => entry.Id)
            .ToListAsync(cancellationToken);
        if (stale.Count > 0)
        {
            await _dbContext.AnalyticsQuestions.Where(entry => stale.Contains(entry.Id)).ExecuteDeleteAsync(cancellationToken);
        }
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => _dbContext.SaveChangesAsync(cancellationToken);
}
