using PurchaseMemory.Application.Products;
using PurchaseMemory.Application.Products.Categories;
using PurchaseMemory.Application.Products.Tags;
using PurchaseMemory.Application.Purchases;
using PurchaseMemory.Domain.Receipts;

namespace PurchaseMemory.Application.ReviewQueue;

/// <summary>
/// Why something is in the «Нужно проверить» queue. Every kind maps to one
/// card type in the UI and to one place where a person can fix it.
/// </summary>
public enum ReviewQueueItemKind
{
    /// <summary>Recognition failed; the receipt page can retry it.</summary>
    FailedReceipt,

    /// <summary>Recognized but never saved as a purchase.</summary>
    UnsavedReceipt,

    /// <summary>A saved purchase whose line totals (with discounts) disagree with the receipt total.</summary>
    TotalMismatch,

    /// <summary>A saved purchase with lines that have no price.</summary>
    MissingPrice,

    /// <summary>A receipt line the matcher could not link to a product on its own.</summary>
    PendingMatch,

    /// <summary>The AI suggested a category and nobody has confirmed it yet.</summary>
    SuggestedCategory,

    /// <summary>Nobody (person or AI) has given the product a category.</summary>
    MissingCategory
}

/// <summary>
/// One entry of the queue. <see cref="Id"/> is stable across calls
/// (kind + entity id), so the UI can key and remove items reliably. Only the
/// fields relevant to the kind are filled; the UI writes the human text.
/// </summary>
public sealed record ReviewQueueItem(
    ReviewQueueItemKind Kind,
    string Id,
    string Title,
    DateTimeOffset OccurredAt,
    Guid? ReceiptId = null,
    Guid? PurchaseId = null,
    Guid? ProductId = null,
    Guid? CategoryId = null,
    string? CategoryName = null,
    string? Currency = null,
    decimal? ReceiptTotal = null,
    decimal? LinesTotal = null,
    int? AffectedLineCount = null,
    // The line to focus when the purchase editor opens (pending match, first missing price).
    int? ItemPosition = null,
    // Pending match only: the products the matcher weighed, best first (at most MaximumCandidates).
    IReadOnlyList<ReviewQueueCandidate>? Candidates = null);

/// <summary>A catalog product the matcher considered for an unmatched line.</summary>
public sealed record ReviewQueueCandidate(Guid ProductId, string Name, string? Brand, Guid? CategoryId);

/// <summary>
/// What the «Нужно проверить» screen narrows the queue to. Same meaning as the
/// feed and catalog filters (<see cref="ProductClassificationFilter"/>): a
/// category includes its subcategories, <see cref="Uncategorized"/> is «без
/// категории», and every tag is required on one product.
/// </summary>
public sealed record ReviewQueueFilter(
    Guid? CategoryId = null,
    bool Uncategorized = false,
    IReadOnlyCollection<Guid>? TagIds = null)
{
    public static ReviewQueueFilter None { get; } = new();
}

public sealed record ReviewQueueGroup(ReviewQueueItemKind Kind, int TotalCount, IReadOnlyList<ReviewQueueItem> Items);

public sealed record ReviewQueueResult(int TotalCount, IReadOnlyList<ReviewQueueGroup> Groups);

// Raw facts read from storage; the service decides what needs attention.
public sealed record ReviewReceiptFact(
    Guid Id,
    ReceiptProcessingStatus Status,
    string? MerchantName,
    DateTimeOffset? PurchasedAt,
    DateTimeOffset UpdatedAt);

public sealed record ReviewPurchaseFact(
    Guid Id,
    Guid? SourceReceiptId,
    string? MerchantName,
    DateTimeOffset? PurchasedAt,
    DateTimeOffset CreatedAt,
    string? Currency,
    decimal? ReceiptTotal,
    IReadOnlyList<PurchaseDraftIssueItem> Items,
    // Catalog products the purchase's lines are linked to; only the tag filter reads them.
    IReadOnlyCollection<Guid>? ProductIds = null);

public sealed record ReviewPendingMatchFact(
    Guid Id,
    string RawItemName,
    DateTimeOffset RaisedAt,
    Guid PurchaseId,
    Guid? SourceReceiptId,
    Guid? BestCandidateProductId,
    int? ItemPosition = null,
    // Best first; products that no longer exist are already left out.
    IReadOnlyList<ReviewQueueCandidate>? Candidates = null);

public sealed record ReviewProductFact(
    Guid Id,
    string Name,
    Guid? CategoryId,
    string? CategoryName,
    Domain.Products.ProductAttributeSource? CategorySource,
    DateTimeOffset UpdatedAt);

public sealed record ReviewQueueFacts(
    IReadOnlyList<ReviewReceiptFact> Receipts,
    IReadOnlyList<ReviewPurchaseFact> Purchases,
    IReadOnlyList<ReviewPendingMatchFact> PendingMatches,
    IReadOnlyList<ReviewProductFact> Products);

public interface IReviewQueueRepository
{
    /// <summary>
    /// Loads failed/unsaved receipts, the most recent <paramref name="maximumPurchases"/>
    /// purchases with their lines, pending matches and products lacking a confirmed category.
    /// </summary>
    Task<ReviewQueueFacts> LoadFactsAsync(int maximumPurchases, CancellationToken cancellationToken);
}

public interface IReviewQueueService
{
    /// <summary>The whole queue — what the navigation badge counts.</summary>
    Task<ReviewQueueResult> GetAsync(int limitPerKind, CancellationToken cancellationToken) =>
        GetAsync(limitPerKind, ReviewQueueFilter.None, cancellationToken);

    /// <summary>
    /// The queue narrowed to items about products carrying every tag in
    /// <paramref name="tagIds"/> (see <see cref="ReviewQueueService.Build"/>).
    /// An unknown tag is <see cref="ProductTagNotFoundException"/>, like the feed filter.
    /// </summary>
    Task<ReviewQueueResult> GetAsync(
        int limitPerKind,
        IReadOnlyCollection<Guid>? tagIds,
        CancellationToken cancellationToken) =>
        GetAsync(limitPerKind, new ReviewQueueFilter(TagIds: tagIds), cancellationToken);

    /// <summary>
    /// The queue narrowed by category and/or tags (see <see cref="ReviewQueueService.Build"/>).
    /// An unknown category is <see cref="ProductCategoryNotFoundException"/>, an
    /// unknown tag <see cref="ProductTagNotFoundException"/>, like the feed filter.
    /// </summary>
    Task<ReviewQueueResult> GetAsync(
        int limitPerKind,
        ReviewQueueFilter filter,
        CancellationToken cancellationToken);
}

/// <summary>
/// Builds the queue from data already stored — no model is called. Data is
/// shared by all approved users in this version, so the queue is too.
/// </summary>
public sealed class ReviewQueueService(
    IReviewQueueRepository repository,
    IProductCatalogRepository catalog,
    IProductCategoryRepository categories,
    IProductTagRepository tags) : IReviewQueueService
{
    public const int DefaultLimitPerKind = 20;
    public const int MaximumLimitPerKind = 100;

    /// <summary>A household never gets near this; it only bounds a runaway scan.</summary>
    public const int MaximumScannedPurchases = 5000;

    /// <summary>How many suggested products a «товар не сопоставлен» card offers.</summary>
    public const int MaximumCandidates = 3;

    public Task<ReviewQueueResult> GetAsync(int limitPerKind, CancellationToken cancellationToken) =>
        GetAsync(limitPerKind, ReviewQueueFilter.None, cancellationToken);

    public Task<ReviewQueueResult> GetAsync(
        int limitPerKind,
        IReadOnlyCollection<Guid>? tagIds,
        CancellationToken cancellationToken) =>
        GetAsync(limitPerKind, new ReviewQueueFilter(TagIds: tagIds), cancellationToken);

    public async Task<ReviewQueueResult> GetAsync(
        int limitPerKind,
        ReviewQueueFilter filter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (limitPerKind is < 0 or > MaximumLimitPerKind)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limitPerKind),
                limitPerKind,
                $"limitPerKind must be between 0 and {MaximumLimitPerKind}.");
        }

        // The same validation as the catalog and feed filters: an unknown
        // category or tag fails, a category brings its subcategories, repeated
        // tags collapse, «no category» together with a category is rejected.
        var classification = await ProductClassificationFilter.ResolveAsync(
            filter.CategoryId, filter.Uncategorized, null, filter.TagIds, categories, tags, cancellationToken);
        var matchingProducts = classification.IsEmpty
            ? null
            : await catalog.ListMatchingProductIdsAsync(classification, cancellationToken);

        var facts = await repository.LoadFactsAsync(MaximumScannedPurchases, cancellationToken);
        var items = Build(facts, matchingProducts);
        var groups = items
            .GroupBy(item => item.Kind)
            .OrderBy(group => group.Key)
            .Select(group => new ReviewQueueGroup(
                group.Key,
                group.Count(),
                group.OrderByDescending(item => item.OccurredAt).ThenBy(item => item.Id, StringComparer.Ordinal)
                    .Take(limitPerKind)
                    .ToList()))
            .ToList();

        return new ReviewQueueResult(items.Count, groups);
    }

    /// <summary>
    /// Categories and tags belong to products, so a queue item matches a
    /// filter only through a catalog product: the product itself for the two
    /// category kinds, and any linked line of the purchase for «сумма не
    /// сходится» and «нет цены» (the same rule as the purchase feed). Receipts
    /// that are not saved yet and lines still waiting to be matched have no
    /// product, so a filter never shows them — not even through a candidate.
    /// <paramref name="matchingProducts"/> is the products passing the filter,
    /// or <c>null</c> for no filter.
    /// </summary>
    public static List<ReviewQueueItem> Build(ReviewQueueFacts facts, IReadOnlySet<Guid>? matchingProducts = null)
    {
        var items = BuildAll(facts);
        if (matchingProducts is null)
        {
            return items;
        }

        var matchingPurchases = facts.Purchases
            .Where(purchase => purchase.ProductIds?.Any(matchingProducts.Contains) == true)
            .Select(purchase => purchase.Id)
            .ToHashSet();
        return items.Where(item => MatchesProducts(item, matchingProducts, matchingPurchases)).ToList();
    }

    private static bool MatchesProducts(ReviewQueueItem item, IReadOnlySet<Guid> matchingProducts, HashSet<Guid> matchingPurchases) =>
        item.Kind switch
        {
            ReviewQueueItemKind.SuggestedCategory or ReviewQueueItemKind.MissingCategory =>
                item.ProductId is { } productId && matchingProducts.Contains(productId),
            ReviewQueueItemKind.TotalMismatch or ReviewQueueItemKind.MissingPrice =>
                item.PurchaseId is { } purchaseId && matchingPurchases.Contains(purchaseId),
            _ => false
        };

    private static List<ReviewQueueItem> BuildAll(ReviewQueueFacts facts)
    {
        var items = new List<ReviewQueueItem>();

        foreach (var receipt in facts.Receipts)
        {
            var kind = receipt.Status switch
            {
                ReceiptProcessingStatus.Failed => ReviewQueueItemKind.FailedReceipt,
                ReceiptProcessingStatus.ReadyForReview => ReviewQueueItemKind.UnsavedReceipt,
                _ => (ReviewQueueItemKind?)null
            };
            if (kind is null)
            {
                continue;
            }

            items.Add(new ReviewQueueItem(
                kind.Value,
                Key(kind.Value, receipt.Id),
                receipt.MerchantName ?? string.Empty,
                receipt.UpdatedAt,
                ReceiptId: receipt.Id));
        }

        foreach (var purchase in facts.Purchases)
        {
            var issues = PurchaseDraftIssueDetector.Detect(purchase.ReceiptTotal, purchase.Currency, purchase.Items);
            var occurredAt = purchase.PurchasedAt ?? purchase.CreatedAt;
            var missing = issues.Count(issue => issue.Type == PurchaseDraftIssueType.MissingItemPrice);
            if (missing > 0)
            {
                items.Add(new ReviewQueueItem(
                    ReviewQueueItemKind.MissingPrice,
                    Key(ReviewQueueItemKind.MissingPrice, purchase.Id),
                    purchase.MerchantName ?? string.Empty,
                    occurredAt,
                    ReceiptId: purchase.SourceReceiptId,
                    PurchaseId: purchase.Id,
                    Currency: purchase.Currency,
                    AffectedLineCount: missing,
                    ItemPosition: issues.First(issue => issue.Type == PurchaseDraftIssueType.MissingItemPrice).ItemPosition));
            }

            if (issues.Any(issue => issue.Type == PurchaseDraftIssueType.ReceiptTotalMismatch))
            {
                items.Add(new ReviewQueueItem(
                    ReviewQueueItemKind.TotalMismatch,
                    Key(ReviewQueueItemKind.TotalMismatch, purchase.Id),
                    purchase.MerchantName ?? string.Empty,
                    occurredAt,
                    ReceiptId: purchase.SourceReceiptId,
                    PurchaseId: purchase.Id,
                    Currency: purchase.Currency,
                    ReceiptTotal: purchase.ReceiptTotal,
                    LinesTotal: purchase.Items.Sum(item => item.LineTotal ?? 0m)));
            }
        }

        foreach (var match in facts.PendingMatches)
        {
            items.Add(new ReviewQueueItem(
                ReviewQueueItemKind.PendingMatch,
                Key(ReviewQueueItemKind.PendingMatch, match.Id),
                match.RawItemName,
                match.RaisedAt,
                ReceiptId: match.SourceReceiptId,
                PurchaseId: match.PurchaseId,
                ProductId: match.BestCandidateProductId,
                ItemPosition: match.ItemPosition,
                Candidates: (match.Candidates ?? []).Take(MaximumCandidates).ToList()));
        }

        foreach (var product in facts.Products)
        {
            if (product.CategorySource == Domain.Products.ProductAttributeSource.Suggested && product.CategoryId is not null)
            {
                items.Add(new ReviewQueueItem(
                    ReviewQueueItemKind.SuggestedCategory,
                    Key(ReviewQueueItemKind.SuggestedCategory, product.Id),
                    product.Name,
                    product.UpdatedAt,
                    ProductId: product.Id,
                    CategoryId: product.CategoryId,
                    CategoryName: product.CategoryName));
            }
            else if (product.CategoryId is null && product.CategorySource != Domain.Products.ProductAttributeSource.Confirmed)
            {
                items.Add(new ReviewQueueItem(
                    ReviewQueueItemKind.MissingCategory,
                    Key(ReviewQueueItemKind.MissingCategory, product.Id),
                    product.Name,
                    product.UpdatedAt,
                    ProductId: product.Id));
            }
        }

        return items;
    }

    private static string Key(ReviewQueueItemKind kind, Guid id) =>
        $"{char.ToLowerInvariant(kind.ToString()[0])}{kind.ToString()[1..]}:{id:D}";
}
