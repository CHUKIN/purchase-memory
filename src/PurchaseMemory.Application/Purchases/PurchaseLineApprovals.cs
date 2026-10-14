using PurchaseMemory.Application.Receipts;

namespace PurchaseMemory.Application.Purchases;

/// <param name="At">When the person swiped, by the phone's clock; null means «now».</param>
public sealed record LineApprovalChange(Guid PurchaseId, Guid LineId, bool Approved, DateTimeOffset? At);

public enum LineApprovalOutcome
{
    /// <summary>The line's mark changed.</summary>
    Applied,

    /// <summary>The line already was in that state (a repeated batch), or a stale undo lost to a newer approval.</summary>
    Unchanged,

    /// <summary>
    /// No such purchase, or no such line in it (e.g. replaced by a correction
    /// in the meantime). Nothing is changed; the phone drops the entry.
    /// </summary>
    NotFound
}

/// <param name="Approved">The line's state after the batch (false for <see cref="LineApprovalOutcome.NotFound"/>).</param>
public sealed record LineApprovalResult(
    Guid PurchaseId,
    Guid LineId,
    LineApprovalOutcome Outcome,
    bool Approved,
    DateTimeOffset? ApprovedAt);

/// <summary>
/// The «checked» marks the swipe review puts on purchase lines. The phone keeps
/// them first and sends them in batches, so a batch must be safe to repeat and
/// must not fail as a whole because one line has gone away.
/// </summary>
public interface IPurchaseLineApprovalService
{
    Task<IReadOnlyList<LineApprovalResult>> ApplyAsync(
        IReadOnlyList<LineApprovalChange> changes,
        ReceiptActor actor,
        CancellationToken cancellationToken);
}

public sealed class PurchaseLineApprovalService(
    IPurchaseDraftRepository purchases,
    TimeProvider timeProvider) : IPurchaseLineApprovalService
{
    /// <summary>Lines in one batch; the phone sends far fewer.</summary>
    public const int MaximumChanges = 200;

    /// <summary>Different purchases in one batch, so a batch cannot load the whole history.</summary>
    public const int MaximumPurchases = 20;

    public async Task<IReadOnlyList<LineApprovalResult>> ApplyAsync(
        IReadOnlyList<LineApprovalChange> changes,
        ReceiptActor actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (changes is null || changes.Count == 0)
        {
            throw new ArgumentException("Send at least one line approval.");
        }

        if (changes.Count > MaximumChanges)
        {
            throw new ArgumentException($"A batch can contain at most {MaximumChanges} line approvals.");
        }

        for (var index = 0; index < changes.Count; index++)
        {
            var change = changes[index];
            if (change is null || change.PurchaseId == Guid.Empty || change.LineId == Guid.Empty)
            {
                throw new ArgumentException($"Approval {index + 1}: purchaseId and lineId are required.");
            }
        }

        if (changes.Select(change => change.PurchaseId).Distinct().Count() > MaximumPurchases)
        {
            throw new ArgumentException($"A batch can touch at most {MaximumPurchases} purchases.");
        }

        var now = timeProvider.GetUtcNow();
        // One state per line: the newest swipe wins inside a batch too.
        var latest = changes
            .Select((change, order) => (Change: change, Order: order, At: Clamp(change.At, now)))
            .GroupBy(entry => entry.Change.LineId)
            .Select(group => group.OrderBy(entry => entry.At).ThenBy(entry => entry.Order).Last())
            .OrderBy(entry => entry.Order)
            .ToList();

        var results = new List<LineApprovalResult>(latest.Count);
        var changed = false;
        foreach (var purchaseGroup in latest.GroupBy(entry => entry.Change.PurchaseId))
        {
            var purchase = await purchases.FindByIdAsync(purchaseGroup.Key, cancellationToken);
            foreach (var (change, _, at) in purchaseGroup)
            {
                var item = purchase?.Items.FirstOrDefault(line => line.Id == change.LineId);
                if (item is null)
                {
                    results.Add(new LineApprovalResult(change.PurchaseId, change.LineId, LineApprovalOutcome.NotFound, false, null));
                    continue;
                }

                var applied = change.Approved
                    ? item.Approve(actor.UserId, actor.Email, at)
                    : item.RevokeApproval(at);
                changed |= applied;
                results.Add(new LineApprovalResult(
                    change.PurchaseId,
                    change.LineId,
                    applied ? LineApprovalOutcome.Applied : LineApprovalOutcome.Unchanged,
                    item.IsApproved,
                    item.ApprovedAt));
            }
        }

        if (changed)
        {
            await purchases.SaveChangesAsync(cancellationToken);
        }

        return results;
    }

    /// <summary>How far back a phone may date a mark (a long offline spell, or a wrong clock).</summary>
    public static readonly TimeSpan MaximumAge = TimeSpan.FromDays(30);

    /// <summary>
    /// The phone's clock within [now − 30 days, now], in whole milliseconds as
    /// storage keeps it, so the first answer equals what a reload shows.
    /// </summary>
    private static DateTimeOffset Clamp(DateTimeOffset? at, DateTimeOffset now)
    {
        var value = at ?? now;
        if (value > now) value = now;
        if (value < now - MaximumAge) value = now - MaximumAge;
        return DateTimeOffset.FromUnixTimeMilliseconds(value.ToUnixTimeMilliseconds());
    }
}
