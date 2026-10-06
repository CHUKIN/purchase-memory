using Microsoft.EntityFrameworkCore;
using PurchaseMemory.Application.Impressions;
using PurchaseMemory.Domain.Impressions;
using PurchaseMemory.Domain.Products;
using PurchaseMemory.Domain.Purchases;
using PurchaseMemory.Domain.Receipts;

namespace PurchaseMemory.Infrastructure.Persistence;

/// <summary>Provider-neutral EF Core queries; both SQLite and Postgres run them.</summary>
public sealed class ProductImpressionRepository(PurchaseMemoryDbContext dbContext) : IProductImpressionRepository
{
    /// <summary>Recent purchases scanned for the «rate recent purchases» block.</summary>
    private const int MaximumRecentPurchases = 100;

    public async Task<ImpressionItemContext?> FindItemContextAsync(Guid purchaseItemId, CancellationToken cancellationToken)
    {
        var row = await dbContext.Set<PurchaseItem>()
            .AsNoTracking()
            .Where(item => item.Id == purchaseItemId)
            .Select(item => new
            {
                item.Id,
                PurchaseId = EF.Property<Guid>(item, "PurchaseDraftId"),
                item.ProductId,
                item.Name,
                item.Kind
            })
            .SingleOrDefaultAsync(cancellationToken);
        return row is null
            ? null
            : new ImpressionItemContext(row.Id, row.PurchaseId, row.ProductId, row.Name, row.Kind == ReceiptLineKind.Discount);
    }

    public void Add(ProductImpression impression)
    {
        ArgumentNullException.ThrowIfNull(impression);
        dbContext.ProductImpressions.Add(impression);
    }

    public Task<ProductImpression?> FindForUpdateAsync(Guid impressionId, CancellationToken cancellationToken) =>
        dbContext.ProductImpressions.SingleOrDefaultAsync(entity => entity.Id == impressionId, cancellationToken);

    public async Task<IReadOnlyList<ImpressionView>> ListAsync(ImpressionFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query =
            from impression in dbContext.ProductImpressions.AsNoTracking()
            where impression.DeletedAt == null
            join purchase in dbContext.PurchaseDrafts.AsNoTracking() on impression.PurchaseId equals purchase.Id
            join line in dbContext.Set<PurchaseItem>().AsNoTracking()
                on impression.PurchaseItemId equals (Guid?)line.Id into lines
            from line in lines.DefaultIfEmpty()
            select new { impression, purchase, line };

        if (filter.ImpressionId is { } impressionId)
        {
            query = query.Where(row => row.impression.Id == impressionId);
        }
        else if (filter.PurchaseItemId is { } purchaseItemId)
        {
            query = query.Where(row => row.impression.PurchaseItemId == purchaseItemId);
        }
        else if (filter.PurchaseId is { } purchaseId)
        {
            query = query.Where(row => row.impression.PurchaseId == purchaseId);
        }
        else if (filter.ProductId is { } productId)
        {
            // The line's current product wins, so relinking a line in the editor
            // moves its impressions with it; a line that is gone falls back to
            // the product captured when the impression was made.
            query = query.Where(row => row.line != null
                ? row.line.ProductId == productId
                : row.impression.ProductId == productId);
        }
        else
        {
            throw new ArgumentException("An impression filter needs a scope.", nameof(filter));
        }

        var rows = await query
            .OrderByDescending(row => row.impression.CreatedAt)
            .Take(Math.Clamp(filter.Limit, 1, ImpressionFilter.MaximumLimit))
            .Select(row => new ImpressionView(
                row.impression.Id,
                row.impression.PurchaseId,
                row.impression.PurchaseItemId,
                row.line != null ? row.line.ProductId : row.impression.ProductId,
                row.line != null ? row.line.Name : row.impression.ItemName,
                row.impression.Rating,
                row.impression.Note,
                row.impression.PhotoStorageKey != null,
                row.impression.AuthorUserId,
                row.impression.AuthorEmail,
                false,
                row.impression.CreatedAt,
                row.purchase.MerchantName,
                row.purchase.PurchasedAt,
                row.purchase.Currency,
                row.line != null ? row.line.Quantity : null,
                row.line != null ? row.line.Unit : null,
                row.line != null ? row.line.UnitPrice : null,
                row.line != null ? row.line.LineTotal : null))
            .ToListAsync(cancellationToken);
        return rows;
    }

    public async Task<IReadOnlyList<RateablePurchaseItemView>> ListUnratedAsync(
        string userId,
        DateTimeOffset since,
        int limit,
        CancellationToken cancellationToken)
    {
        var purchases = await dbContext.PurchaseDrafts
            .AsNoTracking()
            .Include(purchase => purchase.Items)
            .Where(purchase => purchase.PurchasedAt != null ? purchase.PurchasedAt >= since : purchase.CreatedAt >= since)
            .OrderByDescending(purchase => purchase.CreatedAt)
            .Take(MaximumRecentPurchases)
            .ToListAsync(cancellationToken);

        var mine = await (
                from impression in dbContext.ProductImpressions.AsNoTracking()
                where impression.AuthorUserId == userId && impression.DeletedAt == null
                join line in dbContext.Set<PurchaseItem>().AsNoTracking()
                    on impression.PurchaseItemId equals (Guid?)line.Id into lines
                from line in lines.DefaultIfEmpty()
                select new
                {
                    impression.PurchaseItemId,
                    ProductId = line != null ? line.ProductId : impression.ProductId,
                    impression.CreatedAt
                })
            .ToListAsync(cancellationToken);
        var ratedItems = mine.Where(entry => entry.PurchaseItemId is not null).Select(entry => entry.PurchaseItemId!.Value).ToHashSet();
        var latestRatingByProduct = mine
            .Where(entry => entry.ProductId is not null)
            .GroupBy(entry => entry.ProductId!.Value)
            .ToDictionary(group => group.Key, group => group.Max(entry => entry.CreatedAt));

        var candidates = purchases
            .SelectMany(purchase => purchase.Items
                .Where(item => !item.IsDiscount)
                .Select(item => (Purchase: purchase, Item: item, At: purchase.PurchasedAt ?? purchase.CreatedAt)))
            .Where(entry => !ratedItems.Contains(entry.Item.Id))
            // Already rated this product after buying it here: the opinion is
            // fresher than the purchase, so asking again would only nag.
            .Where(entry => entry.Item.ProductId is not { } productId
                || !latestRatingByProduct.TryGetValue(productId, out var ratedAt)
                || ratedAt < entry.At)
            .OrderByDescending(entry => entry.At)
            .ThenBy(entry => entry.Item.Position)
            .ToList();

        // One prompt per product (its latest purchase); unlinked lines by name.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var chosen = new List<(PurchaseDraft Purchase, PurchaseItem Item, DateTimeOffset At)>();
        foreach (var entry in candidates)
        {
            var key = entry.Item.ProductId is { } productId
                ? productId.ToString("N")
                : "name:" + entry.Item.Name.Trim().ToUpperInvariant();
            if (seen.Add(key))
            {
                chosen.Add(entry);
                if (chosen.Count == limit)
                {
                    break;
                }
            }
        }

        var productIds = chosen.Where(entry => entry.Item.ProductId is not null).Select(entry => entry.Item.ProductId!.Value).Distinct().ToList();
        var names = productIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await dbContext.Set<Product>()
                .AsNoTracking()
                .Where(product => productIds.Contains(product.Id))
                .ToDictionaryAsync(product => product.Id, product => product.CanonicalName, cancellationToken);

        return chosen
            .Select(entry => new RateablePurchaseItemView(
                entry.Item.Id,
                entry.Purchase.Id,
                entry.Item.ProductId,
                entry.Item.ProductId is { } productId && names.TryGetValue(productId, out var name) ? name : null,
                entry.Item.Name,
                entry.Purchase.MerchantName,
                entry.At,
                entry.Purchase.Currency,
                entry.Item.UnitPrice,
                entry.Item.LineTotal))
            .ToList();
    }

    public async Task<IReadOnlyList<RateablePurchaseItemView>> ListProductLinesAsync(
        Guid productId,
        int limit,
        CancellationToken cancellationToken)
    {
        var rows = await (
                from line in dbContext.Set<PurchaseItem>().AsNoTracking()
                where line.ProductId == productId
                join purchase in dbContext.PurchaseDrafts.AsNoTracking()
                    on EF.Property<Guid>(line, "PurchaseDraftId") equals purchase.Id
                orderby purchase.CreatedAt descending
                select new { line, purchase })
            .Take(MaximumRecentPurchases)
            .ToListAsync(cancellationToken);
        var name = await dbContext.Set<Product>()
            .AsNoTracking()
            .Where(product => product.Id == productId)
            .Select(product => product.CanonicalName)
            .SingleOrDefaultAsync(cancellationToken);

        return rows
            .Select(row => new RateablePurchaseItemView(
                row.line.Id,
                row.purchase.Id,
                productId,
                name,
                row.line.Name,
                row.purchase.MerchantName,
                row.purchase.PurchasedAt ?? row.purchase.CreatedAt,
                row.purchase.Currency,
                row.line.UnitPrice,
                row.line.LineTotal))
            .OrderByDescending(view => view.PurchasedAt)
            .Take(limit)
            .ToList();
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);

    /// <summary>
    /// Called before a purchase save: impressions on line rows that are about
    /// to be deleted move to the replacement rows (see <see cref="ImpressionRelinking"/>).
    /// </summary>
    internal static async Task RelinkReplacedLinesAsync(PurchaseMemoryDbContext dbContext, CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.DetectChanges();
        var entries = dbContext.ChangeTracker.Entries<PurchaseItem>().ToList();
        var removed = entries
            .Where(entry => entry.State == EntityState.Deleted)
            .GroupBy(entry => (Guid)entry.Property("PurchaseDraftId").OriginalValue!)
            .ToDictionary(group => group.Key, group => group.Select(entry => ImpressionLineRef.From(entry.Entity)).ToList());
        if (removed.Count == 0)
        {
            return;
        }

        var removedIds = removed.Values.SelectMany(lines => lines).Select(line => (Guid?)line.Id).ToList();
        var impressions = await dbContext.ProductImpressions
            .Where(impression => removedIds.Contains(impression.PurchaseItemId))
            .ToListAsync(cancellationToken);
        if (impressions.Count == 0)
        {
            return;
        }

        var added = entries
            .Where(entry => entry.State == EntityState.Added)
            .GroupBy(entry => (Guid)entry.Property("PurchaseDraftId").CurrentValue!)
            .ToDictionary(group => group.Key, group => group.Select(entry => ImpressionLineRef.From(entry.Entity)).ToList());
        var moves = new Dictionary<Guid, Guid>();
        foreach (var (purchaseId, lines) in removed)
        {
            foreach (var (from, to) in ImpressionRelinking.Match(lines, added.GetValueOrDefault(purchaseId) ?? []))
            {
                moves[from] = to;
            }
        }

        foreach (var impression in impressions)
        {
            impression.MoveToItem(moves.TryGetValue(impression.PurchaseItemId!.Value, out var target) ? target : null);
        }
    }
}
