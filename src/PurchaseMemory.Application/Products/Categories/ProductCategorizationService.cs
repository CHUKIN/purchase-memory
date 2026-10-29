using Microsoft.Extensions.Logging;
using PurchaseMemory.Domain.Products;

namespace PurchaseMemory.Application.Products.Categories;

public interface IProductCategorizationService
{
    /// <summary>
    /// Suggests for those of <paramref name="productIds"/> nobody has classified
    /// yet (category, brand) and for those still missing a human name.
    /// </summary>
    Task<ProductCategorizationRunSummary> SuggestForProductsAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Backfill: suggests for up to <paramref name="maximumProducts"/> products
    /// that are unclassified or miss a human name. Each part is only filled
    /// where nobody decided yet. Products the model already answered for are
    /// skipped unless <paramref name="retryAttempted"/>.
    /// </summary>
    Task<ProductCategorizationRunSummary> SuggestForUnclassifiedAsync(
        int maximumProducts,
        bool retryAttempted,
        CancellationToken cancellationToken);

    /// <summary>
    /// «Предложить заново» for one product's human names: asks the model again
    /// and replaces suggested (never confirmed) names with what it says now. A
    /// name the model is unsure about stays as it was.
    /// </summary>
    /// <exception cref="ProductNotFoundException">No such product.</exception>
    /// <exception cref="ProductNamingUnavailableException">No model configured, or the call failed.</exception>
    Task<ProductNamesView> SuggestNamesAsync(Guid productId, CancellationToken cancellationToken);
}

public sealed record ProductCategorizationRunSummary(
    bool Configured,
    int Considered,
    int Suggested,
    int FailedBatches,
    int InputTokens,
    int OutputTokens,
    // Products that got at least one new suggested human name; also counted in Suggested.
    int NamesSuggested = 0)
{
    public static ProductCategorizationRunSummary NotConfigured { get; } = new(false, 0, 0, 0, 0, 0);
}

/// <summary>A product's human names and who decided each.</summary>
public sealed record ProductNamesView(
    Guid ProductId,
    string? NamePl,
    ProductAttributeSource? NamePlSource,
    string? NameRu,
    ProductAttributeSource? NameRuSource)
{
    public static ProductNamesView From(Product product)
    {
        ArgumentNullException.ThrowIfNull(product);
        return new ProductNamesView(product.Id, product.NamePl, product.NamePlSource, product.NameRu, product.NameRuSource);
    }
}

/// <summary>Name suggestions cannot be made right now (no model, or the call failed); the cause is in the log.</summary>
public sealed class ProductNamingUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>
/// Applies model suggestions — category, brand and human names — to products
/// nobody has decided about. It never overwrites a person's decision (the
/// conditional writes guard that too), ignores category ids the model
/// invented, and turns every model failure into a logged, skipped batch.
/// </summary>
public sealed partial class ProductCategorizationService : IProductCategorizationService
{
    public const int MaximumBackfillProducts = 500;

    private readonly IProductCategoryRepository _repository;
    private readonly IProductCategorizer _categorizer;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ProductCategorizationService> _logger;

    public ProductCategorizationService(
        IProductCategoryRepository repository,
        IProductCategorizer categorizer,
        TimeProvider timeProvider,
        ILogger<ProductCategorizationService> logger)
    {
        _repository = repository;
        _categorizer = categorizer;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<ProductCategorizationRunSummary> SuggestForProductsAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(productIds);
        if (productIds.Count == 0 || !_categorizer.IsConfigured)
        {
            return productIds.Count == 0 && _categorizer.IsConfigured
                ? new ProductCategorizationRunSummary(true, 0, 0, 0, 0, 0)
                : ProductCategorizationRunSummary.NotConfigured;
        }

        var products = await _repository.ListProductsToSuggestAsync(productIds.Distinct().ToList(), cancellationToken);
        return await SuggestAsync(products, SuggestionScope.FillMissing(retryAttempted: false), cancellationToken);
    }

    public async Task<ProductCategorizationRunSummary> SuggestForUnclassifiedAsync(
        int maximumProducts,
        bool retryAttempted,
        CancellationToken cancellationToken)
    {
        if (maximumProducts is < 1 or > MaximumBackfillProducts)
        {
            throw new ArgumentException(
                $"Limit must be between 1 and {MaximumBackfillProducts}.",
                nameof(maximumProducts));
        }

        if (!_categorizer.IsConfigured)
        {
            return ProductCategorizationRunSummary.NotConfigured;
        }

        var products = await _repository.ListProductsToSuggestAsync(maximumProducts, retryAttempted, cancellationToken);
        return await SuggestAsync(products, SuggestionScope.FillMissing(retryAttempted), cancellationToken);
    }

    public async Task<ProductNamesView> SuggestNamesAsync(Guid productId, CancellationToken cancellationToken)
    {
        var product = await _repository.FindProductSnapshotAsync(productId, cancellationToken)
            ?? throw new ProductNotFoundException(productId);
        // Both names decided by a person: nothing a model could change, so no paid call.
        if (product.NamePlSource == ProductAttributeSource.Confirmed && product.NameRuSource == ProductAttributeSource.Confirmed)
        {
            return ProductNamesView.From(product);
        }

        if (!_categorizer.IsConfigured)
        {
            throw new ProductNamingUnavailableException("Name suggestions are not configured.");
        }

        var summary = await SuggestAsync([product], SuggestionScope.NamesAgain, cancellationToken);
        if (summary.FailedBatches > 0)
        {
            throw new ProductNamingUnavailableException("The model could not suggest names right now.");
        }

        // Re-read: the conditional writes bypass the change tracker.
        var refreshed = await _repository.FindProductSnapshotAsync(productId, cancellationToken)
            ?? throw new ProductNotFoundException(productId);
        return ProductNamesView.From(refreshed);
    }

    /// <summary>
    /// Which parts of a model answer a run may write. Filling never touches a
    /// value someone (person or model) already gave; «again» replaces suggested
    /// names only and leaves category and brand alone.
    /// </summary>
    private sealed record SuggestionScope(bool Classify, bool RetryAttempted, bool ReplaceSuggestedNames)
    {
        public static SuggestionScope FillMissing(bool retryAttempted) => new(true, retryAttempted, false);

        public static SuggestionScope NamesAgain { get; } = new(false, true, true);

        public bool WantsClassification(Product product) =>
            Classify
            && product.CategorySource is null
            && (RetryAttempted || product.CategorizationAttemptedAt is null);

        public bool WantsNames(Product product) =>
            ReplaceSuggestedNames
                ? product.NamePlSource != ProductAttributeSource.Confirmed || product.NameRuSource != ProductAttributeSource.Confirmed
                : (product.NamePlSource is null || product.NameRuSource is null)
                    && (RetryAttempted || product.NamingAttemptedAt is null);
    }

    private async Task<ProductCategorizationRunSummary> SuggestAsync(
        IReadOnlyList<Product> products,
        SuggestionScope scope,
        CancellationToken cancellationToken)
    {
        if (products.Count == 0)
        {
            return new ProductCategorizationRunSummary(true, 0, 0, 0, 0, 0);
        }

        var categories = await _repository.ListCategoriesAsync(cancellationToken);
        var options = BuildOptions(categories);
        var names = categories.ToDictionary(category => category.Id, category => category.Name);
        var offered = options.Select(option => option.CategoryId).ToHashSet();
        var actor = $"ai:{_categorizer.ModelName}";

        var suggested = 0;
        var namesSuggested = 0;
        var failedBatches = 0;
        var inputTokens = 0;
        var outputTokens = 0;
        var batchSize = Math.Max(1, _categorizer.MaximumBatchSize);

        foreach (var batch in products.Chunk(batchSize))
        {
            ProductCategorizationResult result;
            try
            {
                result = await _categorizer.SuggestAsync(
                    batch.Select(product => new ProductToCategorize(
                        product.Id,
                        product.CanonicalName,
                        product.Brand,
                        // A person's name in one language anchors the other; only missing ones are written anyway.
                        product.NamePlSource == ProductAttributeSource.Confirmed ? product.NamePl : null,
                        product.NameRuSource == ProductAttributeSource.Confirmed ? product.NameRu : null)).ToList(),
                    options,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // Any failure — network, refusal, malformed output — leaves the
                // batch uncategorized for the next run instead of surfacing.
                failedBatches++;
                BatchFailed(exception, batch.Length, exception.GetType().Name);
                continue;
            }

            inputTokens += result.InputTokens ?? 0;
            outputTokens += result.OutputTokens ?? 0;

            var byId = batch.ToDictionary(product => product.Id);
            var now = _timeProvider.GetUtcNow();
            var answers = new Dictionary<Guid, ProductCategorySuggestion>();
            foreach (var suggestion in result.Suggestions)
            {
                if (byId.ContainsKey(suggestion.ProductId))
                {
                    answers.TryAdd(suggestion.ProductId, suggestion);
                }
            }

            // Every product of an answered batch counts as attempted, including
            // ones the model skipped or placed in an invented category.
            foreach (var product in batch)
            {
                answers.TryGetValue(product.Id, out var suggestion);
                var changed = false;
                if (scope.WantsClassification(product))
                {
                    changed |= await ApplyClassificationAsync(product, suggestion, offered, names, actor, now, cancellationToken);
                }

                if (scope.WantsNames(product))
                {
                    var named = await ApplyNamesAsync(product, suggestion, scope.ReplaceSuggestedNames, actor, now, cancellationToken);
                    if (named)
                    {
                        namesSuggested++;
                    }

                    changed |= named;
                }

                if (changed)
                {
                    suggested++;
                }
            }

            await _repository.SaveChangesAsync(cancellationToken);
        }

        RunCompleted(products.Count, suggested, namesSuggested, failedBatches, inputTokens, outputTokens);
        return new ProductCategorizationRunSummary(
            true, products.Count, suggested, failedBatches, inputTokens, outputTokens, namesSuggested);
    }

    private async Task<bool> ApplyClassificationAsync(
        Product product,
        ProductCategorySuggestion? suggestion,
        HashSet<Guid> offered,
        Dictionary<Guid, string> names,
        string actor,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var categoryId = suggestion?.CategoryId is { } id && offered.Contains(id) ? id : (Guid?)null;
        if (suggestion?.CategoryId is not null && categoryId is null)
        {
            UnknownCategoryIgnored(product.Id);
        }

        var brand = Product.CleanBrand(suggestion?.Brand);
        var written = await _repository.ApplySuggestionAsync(
            product.Id,
            categoryId,
            brand,
            ProductNameNormalizer.Normalize(brand),
            now,
            cancellationToken);

        // Audit only what the conditional write actually changed; the
        // snapshot supplies the old value.
        if (written.CategoryWritten)
        {
            AddAudit(product.Id, ProductAuditEntry.CategoryField,
                ProductCategoryService.Describe(product.CategoryId, product.CategorySource, names),
                ProductCategoryService.Describe(categoryId, ProductAttributeSource.Suggested, names), actor, now);
        }

        if (written.BrandWritten)
        {
            AddAudit(product.Id, ProductAuditEntry.BrandField,
                ProductCategoryService.DescribeBrand(product.Brand, product.BrandSource),
                ProductCategoryService.DescribeBrand(brand, ProductAttributeSource.Suggested), actor, now);
        }

        return written.CategoryWritten || written.BrandWritten;
    }

    private async Task<bool> ApplyNamesAsync(
        Product product,
        ProductCategorySuggestion? suggestion,
        bool replaceSuggested,
        string actor,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var names = ProductNameSuggestion.From(suggestion?.NamePl, suggestion?.NameRu);
        var written = await _repository.ApplyNameSuggestionAsync(product.Id, names, replaceSuggested, now, cancellationToken);
        if (written.PolishWritten)
        {
            AddAudit(product.Id, ProductAuditEntry.NamePlField,
                ProductCategoryService.DescribeName(product.NamePl, product.NamePlSource),
                ProductCategoryService.DescribeName(names.NamePl, ProductAttributeSource.Suggested), actor, now);
        }

        if (written.RussianWritten)
        {
            AddAudit(product.Id, ProductAuditEntry.NameRuField,
                ProductCategoryService.DescribeName(product.NameRu, product.NameRuSource),
                ProductCategoryService.DescribeName(names.NameRu, ProductAttributeSource.Suggested), actor, now);
        }

        return written.PolishWritten || written.RussianWritten;
    }

    private void AddAudit(Guid productId, string field, string oldValue, string newValue, string actor, DateTimeOffset now)
    {
        _repository.AddAuditEntry(ProductAuditEntry.Create(
            productId, actor, null, field, ProductAuditChangeSource.Suggestion, oldValue, newValue, now));
    }

    /// <summary>Every category with its path; the model may pick a group or a subcategory.</summary>
    private static List<CategoryOption> BuildOptions(IReadOnlyList<ProductCategory> categories)
    {
        var names = categories.ToDictionary(category => category.Id, category => category.Name);
        return categories
            .OrderBy(category => category.ParentId is null ? category.Position : categories.First(parent => parent.Id == category.ParentId).Position)
            .ThenBy(category => category.ParentId is null ? -1 : category.Position)
            .Select(category => new CategoryOption(
                category.Id,
                category.ParentId is { } parentId && names.TryGetValue(parentId, out var parentName)
                    ? $"{parentName} / {category.Name}"
                    : category.Name))
            .ToList();
    }

    [LoggerMessage(EventId = 3301, Level = LogLevel.Warning,
        Message = "Product categorization batch of {ProductCount} failed ({ErrorType}); products stay uncategorized")]
    private partial void BatchFailed(Exception exception, int productCount, string errorType);

    [LoggerMessage(EventId = 3302, Level = LogLevel.Information,
        Message = "Model suggested an unknown category for product {ProductId}; ignored")]
    private partial void UnknownCategoryIgnored(Guid productId);

    [LoggerMessage(EventId = 3303, Level = LogLevel.Information,
        Message = "Product categorization considered {Considered} products, suggested {Suggested} (names for {NamesSuggested}), failed batches {FailedBatches}; input tokens {InputTokens}; output tokens {OutputTokens}")]
    private partial void RunCompleted(int considered, int suggested, int namesSuggested, int failedBatches, int inputTokens, int outputTokens);
}
