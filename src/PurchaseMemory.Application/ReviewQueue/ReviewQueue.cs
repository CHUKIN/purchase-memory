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

    /// <summary>
    /// A product the matcher created with the «проверить» flag: it looks like
    /// one or more existing products, and a person says whether it is one of
    /// them (merge) or a product of its own. When it has no category either
    /// (<see cref="ReviewQueueItem.NeedsCategory"/>) or only an AI-suggested
    /// one (<see cref="ReviewQueueItem.CategoryId"/> and
    /// <see cref="ReviewQueueItem.CategoryName"/> set), this one item also
    /// carries that step instead of a second <see cref="MissingCategory"/> or
    /// <see cref="SuggestedCategory"/> item.
    /// </summary>
    ProductNeedsReview,

    /// <summary>
    /// The AI suggested a category and nobody has confirmed it yet. Never for
    /// a flagged product: its <see cref="ProductNeedsReview"/> item says so.
    /// </summary>
    SuggestedCategory,

    /// <summary>
    /// Nobody (person or AI) has given the product a category. Never for a
    /// flagged product: its <see cref="ProductNeedsReview"/> item says so.
    /// </summary>
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
    // Suggested category; for a flagged product only when its category is AI-suggested.
    Guid? CategoryId = null,
    string? CategoryName = null,
    string? Currency = null,
    decimal? ReceiptTotal = null,
    decimal? LinesTotal = null,
    int? AffectedLineCount = null,
    // The line to focus when the purchase editor opens (first missing price).
    int? ItemPosition = null,
    // Flagged product only: the products it might really be, best first (at most MaximumCandidates).
    IReadOnlyList<ReviewQueueCandidate>? Candidates = null,
    // Flagged product only: distinct purchases it appears on.
    int? PurchaseCount = null,
    // Flagged product only: it has no category either, so the card also asks for one.
    bool NeedsCategory = false);

/// <summary>An existing product a flagged product might really be.</summary>
public sealed record ReviewQueueCandidate(
    Guid ProductId,
    string Name,
    string? Brand,
    Guid? CategoryId,
    int PurchaseCount = 0,
    double Score = 0);

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

public sealed record ReviewFlaggedProductFact(
    Guid Id,
    string Name,
    DateTimeOffset CreatedAt,
    int PurchaseCount,
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
    IReadOnlyList<ReviewFlaggedProductFact> FlaggedProducts,
    IReadOnlyList<ReviewProductFact> Products);

public interface IReviewQueueRepository
{
    /// <summary>
    /// Loads failed/unsaved receipts, the most recent <paramref name="maximumPurchases"/>
    /// purchases with their lines, flagged products and products lacking a confirmed category.
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

    /// <summary>How many candidate products a «нужно проверить товар» card offers.</summary>
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
    /// category kinds and for a flagged product (never through a candidate),
    /// and any linked line of the purchase for «сумма не сходится» and «нет
    /// цены» (the same rule as the purchase feed). Receipts that are not saved
    /// yet have no product, so a filter never shows them.
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
            ReviewQueueItemKind.SuggestedCategory or ReviewQueueItemKind.MissingCategory or ReviewQueueItemKind.ProductNeedsReview =>
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

        // One product, one item: a flagged product whose category is missing
        // or only suggested carries that step on its own card, so the badge
        // counts it once. Its category items below are skipped.
        var flaggedIds = facts.FlaggedProducts.Select(product => product.Id).ToHashSet();
        var categoryFacts = facts.Products.ToDictionary(product => product.Id);

        foreach (var flagged in facts.FlaggedProducts)
        {
            var category = categoryFacts.GetValueOrDefault(flagged.Id);
            var suggested = category is not null && IsSuggestedCategory(category);
            items.Add(new ReviewQueueItem(
                ReviewQueueItemKind.ProductNeedsReview,
                Key(ReviewQueueItemKind.ProductNeedsReview, flagged.Id),
                flagged.Name,
                flagged.CreatedAt,
                ProductId: flagged.Id,
                CategoryId: suggested ? category!.CategoryId : null,
                CategoryName: suggested ? category!.CategoryName : null,
                Candidates: (flagged.Candidates ?? []).Take(MaximumCandidates).ToList(),
                PurchaseCount: flagged.PurchaseCount,
                NeedsCategory: category is not null && IsMissingCategory(category)));
        }

        foreach (var product in facts.Products.Where(product => !flaggedIds.Contains(product.Id)))
        {
            if (IsSuggestedCategory(product))
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
            else if (IsMissingCategory(product))
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

    private static bool IsSuggestedCategory(ReviewProductFact product) =>
        product.CategorySource == Domain.Products.ProductAttributeSource.Suggested && product.CategoryId is not null;

    private static bool IsMissingCategory(ReviewProductFact product) =>
        product.CategoryId is null && product.CategorySource != Domain.Products.ProductAttributeSource.Confirmed;

    private static string Key(ReviewQueueItemKind kind, Guid id) =>
        $"{char.ToLowerInvariant(kind.ToString()[0])}{kind.ToString()[1..]}:{id:D}";
}
