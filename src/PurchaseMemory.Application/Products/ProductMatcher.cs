using PurchaseMemory.Domain.Products;
using PurchaseMemory.Domain.Purchases;

namespace PurchaseMemory.Application.Products;

/// <summary>
/// Deterministic matcher: normalization, an exact store-scoped alias lookup and
/// bigram similarity. No network, no model, no randomness — the same purchase
/// always resolves the same way, which is what makes the catalog reviewable.
/// </summary>
public sealed class ProductMatcher : IProductMatcher
{
    /// <summary>
    /// Upper bound for the in-memory catalog scan. A two-person household will
    /// not approach it; the cap only keeps a pathological database from turning
    /// one save into an unbounded scan.
    /// </summary>
    private const int MaximumCatalogSize = 20_000;

    /// <summary>Audit actor for decisions the matcher makes on its own.</summary>
    public const string SystemActor = "system";

    private readonly IProductCatalogRepository _repository;
    private readonly TimeProvider _timeProvider;

    public ProductMatcher(IProductCatalogRepository repository, TimeProvider timeProvider)
    {
        _repository = repository;
        _timeProvider = timeProvider;
    }

    public async Task<ProductMatchSummary> MatchAsync(
        PurchaseDraft purchase,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(purchase);

        var items = purchase.Items.OrderBy(item => item.Position).ToList();
        if (items.Count == 0)
        {
            return ProductMatchSummary.Empty;
        }

        var merchantKey = ProductNameNormalizer.NormalizeMerchant(purchase.MerchantName);
        var now = _timeProvider.GetUtcNow();

        var aliases = await _repository.ListAliasesForMerchantAsync(merchantKey, cancellationToken);
        var aliasesByText = new Dictionary<string, ProductAlias>(StringComparer.Ordinal);
        foreach (var alias in aliases)
        {
            aliasesByText.TryAdd(alias.NormalizedText, alias);
        }

        var catalog = (await _repository.ListCatalogAsync(MaximumCatalogSize, cancellationToken)).ToList();
        var merchantProductIds = aliases.Select(alias => alias.ProductId).ToHashSet();

        var summary = ProductMatchSummary.Empty;
        foreach (var item in items)
        {
            var outcome = Resolve(item, merchantKey, aliasesByText, catalog, merchantProductIds, now);
            summary = summary.Add(outcome);
        }

        await _repository.SaveChangesAsync(cancellationToken);
        return summary;
    }

    private ProductMatchOutcome Resolve(
        PurchaseItem item,
        string merchantKey,
        Dictionary<string, ProductAlias> aliasesByText,
        List<Product> catalog,
        HashSet<Guid> merchantProductIds,
        DateTimeOffset now)
    {
        if (item.ProductId is not null)
        {
            return ProductMatchOutcome.AlreadyLinked;
        }

        // A discount line ("OPUST …") is money off a product, not a product:
        // it must never create a catalog entry, an alias or a review question.
        if (item.IsDiscount)
        {
            return ProductMatchOutcome.Skipped;
        }

        var normalized = ProductNameNormalizer.Normalize(item.Name);
        if (normalized.Length == 0)
        {
            return ProductMatchOutcome.Skipped;
        }

        if (aliasesByText.TryGetValue(normalized, out var alias))
        {
            item.LinkToProduct(alias.ProductId);
            return ProductMatchOutcome.AliasHit;
        }

        // Same-store history is the strongest signal, so it is scored first and
        // only a barren result falls back to the whole catalog.
        var decision = Decide(normalized, catalog.Where(product => merchantProductIds.Contains(product.Id)));
        if (decision.Kind == DecisionKind.NoCandidate)
        {
            decision = Decide(normalized, catalog);
        }

        switch (decision.Kind)
        {
            case DecisionKind.Confident:
                {
                    var productId = decision.Candidates[0].Product.Id;
                    item.LinkToProduct(productId);
                    RecordAlias(merchantKey, normalized, item.Name, productId, ProductAliasSource.AutoMatched, now, merchantProductIds, aliasesByText);
                    return ProductMatchOutcome.AutoMatched;
                }

            case DecisionKind.Ambiguous:
                {
                    // Doubt never leaves a line without a product: it gets one
                    // of its own, flagged, with the candidates it might really
                    // be. A wrong merge is harder to undo than a duplicate, so
                    // the person decides later from the flag.
                    var product = CreateProduct(item, normalized, catalog, now);
                    product.FlagForReview(ProductReviewReason.AmbiguousMatch, now);
                    _repository.AddReviewCandidates(decision.Candidates
                        .Take(ProductMatchingThresholds.MaximumRecordedCandidates)
                        .Select((candidate, position) => ProductReviewCandidate.Create(
                            product.Id,
                            position,
                            candidate.Product.Id,
                            candidate.Score))
                        .ToList());
                    _repository.AddAuditEntry(ProductAuditEntry.Create(
                        product.Id,
                        SystemActor,
                        null,
                        ProductAuditEntry.ReviewField,
                        ProductAuditChangeSource.Matching,
                        null,
                        nameof(ProductReviewReason.AmbiguousMatch),
                        now));
                    RecordAlias(merchantKey, normalized, item.Name, product.Id, ProductAliasSource.AutoCreated, now, merchantProductIds, aliasesByText);
                    return ProductMatchOutcome.FlaggedForReview;
                }

            default:
                {
                    var product = CreateProduct(item, normalized, catalog, now);
                    RecordAlias(merchantKey, normalized, item.Name, product.Id, ProductAliasSource.AutoCreated, now, merchantProductIds, aliasesByText);
                    return ProductMatchOutcome.AutoCreated;
                }
        }
    }

    private Product CreateProduct(PurchaseItem item, string normalized, List<Product> catalog, DateTimeOffset now)
    {
        var product = Product.Create(item.Name, normalized, now, ProductOrigin.Receipt);
        _repository.AddProduct(product);
        catalog.Add(product);
        item.LinkToProduct(product.Id);
        return product;
    }

    private void RecordAlias(
        string merchantKey,
        string normalizedText,
        string rawText,
        Guid productId,
        ProductAliasSource source,
        DateTimeOffset now,
        HashSet<Guid> merchantProductIds,
        Dictionary<string, ProductAlias> aliasesByText)
    {
        var alias = ProductAlias.Create(merchantKey, normalizedText, rawText, productId, source, now);
        _repository.AddAlias(alias);

        // Keep the in-memory view consistent so repeated lines inside one receipt
        // reuse the alias instead of creating a duplicate.
        aliasesByText[normalizedText] = alias;
        merchantProductIds.Add(productId);
    }

    private static Decision Decide(string normalized, IEnumerable<Product> products)
    {
        var scored = products
            .Select(product => new ScoredCandidate(product, ProductSimilarity.Score(normalized, product.NormalizedName)))
            .Where(candidate => candidate.Score >= ProductMatchingThresholds.PlausibleCandidate)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Product.NormalizedName, StringComparer.Ordinal)
            .ToList();

        if (scored.Count == 0)
        {
            return new Decision(DecisionKind.NoCandidate, scored);
        }

        var best = scored[0];
        var runnerUpScore = scored.Count > 1 ? scored[1].Score : 0;
        var dominant = scored.Count == 1
            || best.Score - runnerUpScore >= ProductMatchingThresholds.DominanceMargin;
        var sizesAgree = ProductSimilarity.NumericSignaturesMatch(normalized, best.Product.NormalizedName);

        return best.Score >= ProductMatchingThresholds.ConfidentMatch && dominant && sizesAgree
            ? new Decision(DecisionKind.Confident, scored)
            : new Decision(DecisionKind.Ambiguous, scored);
    }

    private enum DecisionKind
    {
        NoCandidate,
        Confident,
        Ambiguous
    }

    private sealed record ScoredCandidate(Product Product, double Score);

    private sealed record Decision(DecisionKind Kind, IReadOnlyList<ScoredCandidate> Candidates);
}
