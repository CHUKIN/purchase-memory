using PurchaseMemory.Application.Products;
using PurchaseMemory.Application.Products.Categories;
using PurchaseMemory.Application.Products.Tags;
using PurchaseMemory.Application.Receipts;
using PurchaseMemory.Domain.Purchases;
using PurchaseMemory.Domain.Receipts;

namespace PurchaseMemory.Application.Purchases;

public sealed class PurchaseDraftService : IPurchaseDraftService
{
    private const int MaximumSearchResults = 10;
    private const int MaximumDuplicateCandidates = 50;

    // Review status and Unicode-aware free-text matching are computed above the
    // persistence boundary, so the feed pages a bounded candidate window instead
    // of paging in SQL. The window is reported back through
    // <see cref="PurchaseFeedPage.Truncated"/> and disappears once PostgreSQL
    // full-text search replaces the SQLite prototype.
    private const int MaximumFeedCandidates = 1_000;

    private readonly IPurchaseDraftRepository _repository;
    private readonly IProductMatcher _productMatcher;
    private readonly IProductCategorizationDispatcher _categorization;
    private readonly TimeProvider _timeProvider;
    private readonly IProductCatalogRepository _catalog;
    private readonly IProductCategoryRepository _categories;
    private readonly IProductTagRepository _tags;

    public PurchaseDraftService(
        IPurchaseDraftRepository repository,
        IProductMatcher productMatcher,
        IProductCategorizationDispatcher categorization,
        TimeProvider timeProvider,
        IProductCatalogRepository catalog,
        IProductCategoryRepository categories,
        IProductTagRepository tags)
    {
        _catalog = catalog;
        _categories = categories;
        _tags = tags;
        _repository = repository;
        _productMatcher = productMatcher;
        _categorization = categorization;
        _timeProvider = timeProvider;
    }

    public async Task<PurchaseDraftView> CreateAsync(
        CreatePurchaseDraftCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.SourceReceiptId is not null)
        {
            var existingSourcePurchase = await _repository.FindBySourceReceiptIdAsync(
                command.SourceReceiptId.Value,
                cancellationToken);

            if (existingSourcePurchase is not null)
            {
                return CreateView(existingSourcePurchase);
            }
        }

        var now = _timeProvider.GetUtcNow();
        var draft = PurchaseDraft.Create(
            command.MerchantName,
            command.PurchasedAt,
            command.Currency,
            command.ReceiptTotal,
            CreateItems(command.Items),
            now,
            command.SourceReceiptId);

        if (!command.AllowPossibleDuplicate)
        {
            var candidates = await _repository.FindPossibleDuplicateCandidatesAsync(
                draft.PurchasedAt,
                draft.ReceiptTotal,
                MaximumDuplicateCandidates,
                cancellationToken);

            var duplicate = candidates.FirstOrDefault(candidate => AreEquivalent(candidate, draft));
            if (duplicate is not null)
            {
                throw new PossibleDuplicatePurchaseException(duplicate.Id);
            }
        }

        _repository.Add(draft);
        await _repository.SaveChangesAsync(cancellationToken);

        // Every save path — the REST review flow and the MCP tool alike — reaches
        // the catalog through this single seam, so matching cannot be forgotten
        // by whoever adds the next interface.
        await _productMatcher.MatchAsync(draft, cancellationToken);
        RequestCategorization(draft);

        return CreateView(draft);
    }

    /// <summary>
    /// Hands the linked products to background categorization. The dispatcher
    /// only filters out already classified products later, so it is cheap to
    /// pass them all; it never waits, so saving is not slowed by a model call.
    /// </summary>
    private void RequestCategorization(PurchaseDraft draft)
    {
        var productIds = draft.Items
            .Where(item => item.ProductId is not null)
            .Select(item => item.ProductId!.Value)
            .Distinct()
            .ToList();
        if (productIds.Count > 0)
        {
            _categorization.Enqueue(productIds);
        }
    }

    public async Task<PurchaseDraftView> UpdateAsync(
        UpdatePurchaseDraftCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var draft = await _repository.FindByIdAsync(command.PurchaseId, cancellationToken)
            ?? throw new PurchaseDraftNotFoundException(command.PurchaseId);

        draft.ReplaceDetails(
            command.MerchantName,
            command.PurchasedAt,
            command.Currency,
            command.ReceiptTotal,
            CreateItems(command.Items),
            _timeProvider.GetUtcNow());

        await _repository.SaveChangesAsync(cancellationToken);

        // A correction replaces the item rows outright, so the catalog links have
        // to be recomputed for the new ones.
        await _productMatcher.MatchAsync(draft, cancellationToken);
        RequestCategorization(draft);

        return CreateView(draft);
    }

    public async Task<IReadOnlyList<PurchaseDraftView>> SearchAsync(
        string? query,
        CancellationToken cancellationToken)
    {
        if (query?.Length > 200)
        {
            throw new ArgumentException("Search query cannot exceed 200 characters.", nameof(query));
        }

        var drafts = await _repository.SearchAsync(query?.Trim(), MaximumSearchResults, cancellationToken);
        return drafts.Select(CreateView).ToList();
    }

    public async Task<PurchaseFeedPage> GetFeedAsync(
        PurchaseFeedQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var text = query.Query?.Trim();
        if (text?.Length > PurchaseFeedQuery.MaximumQueryLength)
        {
            throw new ArgumentException(
                $"Search query cannot exceed {PurchaseFeedQuery.MaximumQueryLength} characters.",
                nameof(query));
        }

        if (query.Page < 1)
        {
            throw new ArgumentException("Page must be 1 or greater.", nameof(query));
        }

        if (query.PageSize is < 1 || query.PageSize > PurchaseFeedQuery.MaximumPageSize)
        {
            throw new ArgumentException(
                $"Page size must be between 1 and {PurchaseFeedQuery.MaximumPageSize}.",
                nameof(query));
        }

        if (query.From is not null && query.To is not null && query.From > query.To)
        {
            throw new ArgumentException("The date range start cannot be after its end.", nameof(query));
        }

        var classification = await ProductClassificationFilter.ResolveAsync(
            query.CategoryId, query.Uncategorized, query.Brand, query.TagIds, _categories, _tags, cancellationToken);
        var matchingProducts = classification.IsEmpty
            ? null
            : await _catalog.ListMatchingProductIdsAsync(classification, cancellationToken);

        var candidates = await _repository.ListFeedCandidatesAsync(
            query.From,
            query.To,
            MaximumFeedCandidates,
            cancellationToken);

        var matches = candidates
            .Where(draft => matchingProducts is null
                || draft.Items.Any(item => item.ProductId is { } id && matchingProducts.Contains(id)))
            .Select(draft => CreateFilteredView(draft, matchingProducts))
            .Where(view => MatchesText(view, text) && MatchesStatus(view, query.Status))
            .ToList();

        var items = matches
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToList();

        return new PurchaseFeedPage(
            items,
            query.Page,
            query.PageSize,
            matches.Count,
            query.Page * query.PageSize < matches.Count,
            candidates.Count >= MaximumFeedCandidates);
    }

    public async Task<PurchaseDraftView?> FindBySourceReceiptIdAsync(
        Guid sourceReceiptId,
        CancellationToken cancellationToken)
    {
        if (sourceReceiptId == Guid.Empty)
        {
            throw new ArgumentException("Source receipt identifier cannot be empty.", nameof(sourceReceiptId));
        }

        var draft = await _repository.FindBySourceReceiptIdAsync(sourceReceiptId, cancellationToken);
        return draft is null ? null : CreateView(draft);
    }

    internal static List<PurchaseItem> CreateItems(IReadOnlyList<PurchaseItemCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);

        var kinds = commands
            .Select(item => ReceiptLineKindResolver.Resolve(item.Kind, item.LineTotal))
            .ToList();
        var links = ReceiptDiscountLinking.Resolve(commands
            .Select((item, position) => new ReceiptLineForLinking(kinds[position], item.Name, item.LineTotal))
            .ToList());
        return commands
            .Select((item, position) => PurchaseItem.Create(
                position,
                item.Name,
                item.Quantity,
                item.Unit,
                item.UnitPrice,
                item.LineTotal,
                kinds[position],
                links[position].TargetPosition))
            .ToList();
    }

    private static PurchaseDraftView CreateView(PurchaseDraft draft) => CreateFilteredView(draft, null);

    private static PurchaseDraftView CreateFilteredView(PurchaseDraft draft, IReadOnlySet<Guid>? matchingProducts)
    {
        var items = draft.Items
            .OrderBy(item => item.Position)
            .Select(item => new PurchaseItemView(
                item.Name,
                item.Quantity,
                item.Unit,
                item.UnitPrice,
                item.LineTotal,
                item.Kind,
                item.DiscountedItemPosition,
                matchingProducts is null
                    ? null
                    : item.ProductId is { } id && matchingProducts.Contains(id)))
            .ToList();

        return new PurchaseDraftView(
            draft.Id,
            draft.MerchantName,
            draft.PurchasedAt,
            draft.Currency,
            draft.ReceiptTotal,
            draft.CreatedAt,
            draft.UpdatedAt,
            items,
            PurchaseDraftIssueDetector.Detect(
                draft.ReceiptTotal,
                draft.Currency,
                draft.Items
                    .OrderBy(item => item.Position)
                    .Select(item => new PurchaseDraftIssueItem(
                        item.Position,
                        item.Name,
                        item.UnitPrice,
                        item.LineTotal,
                        Kind: item.Kind))
                    .ToList()),
            draft.SourceReceiptId);
    }

    private static bool AreEquivalent(PurchaseDraft existing, PurchaseDraft candidate)
    {
        if (!TextEquals(existing.MerchantName, candidate.MerchantName)
            || existing.PurchasedAt != candidate.PurchasedAt
            || !TextEquals(existing.Currency, candidate.Currency)
            || existing.ReceiptTotal != candidate.ReceiptTotal)
        {
            return false;
        }

        var existingItems = existing.Items.OrderBy(item => item.Position).ToList();
        var candidateItems = candidate.Items.OrderBy(item => item.Position).ToList();

        return existingItems.Count == candidateItems.Count
            && existingItems.Zip(candidateItems).All(pair =>
                TextEquals(pair.First.Name, pair.Second.Name)
                && pair.First.Quantity == pair.Second.Quantity
                && TextEquals(pair.First.Unit, pair.Second.Unit)
                && pair.First.UnitPrice == pair.Second.UnitPrice
                && pair.First.LineTotal == pair.Second.LineTotal
                && pair.First.Kind == pair.Second.Kind);
    }

    private static bool MatchesText(PurchaseDraftView view, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        return Contains(view.MerchantName, text)
            || view.Items.Any(item => Contains(item.Name, text));
    }

    private static bool MatchesStatus(PurchaseDraftView view, PurchaseReviewStatus status)
    {
        return status switch
        {
            PurchaseReviewStatus.NeedsAttention => view.Issues.Count > 0,
            PurchaseReviewStatus.Reviewed => view.Issues.Count == 0,
            _ => true
        };
    }

    private static bool Contains(string? source, string text)
    {
        return source?.Contains(text, StringComparison.OrdinalIgnoreCase) is true;
    }

    private static bool TextEquals(string? left, string? right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }
}
