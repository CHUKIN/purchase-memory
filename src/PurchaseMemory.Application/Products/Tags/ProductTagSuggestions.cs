using Microsoft.Extensions.Logging;
using PurchaseMemory.Application.Products.Categories;
using PurchaseMemory.Domain.Products;

namespace PurchaseMemory.Application.Products.Tags;

/// <summary>
/// AI hints about which of the household's existing tags fit a product. A
/// suggestion is never applied here: the person accepts it through the usual
/// product-tags call. New tags are never invented — the model only chooses
/// among tags people created.
/// </summary>
public interface IProductTagSuggestionService
{
    /// <exception cref="ProductNotFoundException">No such product.</exception>
    /// <exception cref="TagSuggestionsUnavailableException">The model is not configured or did not answer.</exception>
    Task<ProductTagSuggestions> SuggestAsync(Guid productId, string? locale, CancellationToken cancellationToken);
}

/// <summary>The provider-neutral model call behind tag suggestions.</summary>
public interface ITagSuggester
{
    /// <summary>Whether a call can be made at all (switched on, model and credential configured).</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Picks tags from <paramref name="tags"/> that fit the product. The result
    /// may still contain anything; the caller filters it against the offer.
    /// </summary>
    /// <exception cref="TagSuggestionsUnavailableException">Any provider failure.</exception>
    Task<IReadOnlyList<SuggestedTag>> SuggestAsync(
        TagSuggestionProduct product,
        IReadOnlyList<TagSuggestionOption> tags,
        TagSuggestionLanguage language,
        CancellationToken cancellationToken);
}

/// <summary>What the model is told about the product. Receipt wording and stores help with short names.</summary>
public sealed record TagSuggestionProduct(
    string Name,
    string? Brand,
    string? CategoryPath,
    IReadOnlyList<string> ReceiptTexts,
    IReadOnlyList<string> Merchants);

public sealed record TagSuggestionOption(Guid TagId, string Name);

public sealed record SuggestedTag(Guid TagId, string Reason);

/// <summary>Language of the short reason shown next to each suggestion.</summary>
public enum TagSuggestionLanguage
{
    Russian,
    English
}

/// <param name="OfferedTagCount">
/// Household tags that could be suggested (not yet on the product). Zero means
/// the model was not asked: there is nothing to choose from.
/// </param>
public sealed record ProductTagSuggestions(
    Guid ProductId,
    IReadOnlyList<ProductTagSuggestion> Suggestions,
    int OfferedTagCount);

public sealed record ProductTagSuggestion(Guid TagId, string Name, string Reason);

/// <summary>Suggestions cannot be made right now; the rest of the app is unaffected.</summary>
public sealed class TagSuggestionsUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed partial class ProductTagSuggestionService : IProductTagSuggestionService
{
    /// <summary>Upper bound on what the person is shown, whatever the model returns.</summary>
    public const int MaximumSuggestions = 5;

    /// <summary>Receipt wordings and stores sent as context; a few are enough.</summary>
    private const int MaximumContextEntries = 5;

    /// <summary>Longest reason kept; the prompt asks for a short phrase.</summary>
    public const int MaximumReasonLength = 160;

    private readonly IProductCatalogService _catalog;
    private readonly IProductTagRepository _tags;
    private readonly IProductCategoryRepository _categories;
    private readonly ITagSuggester _suggester;
    private readonly ILogger<ProductTagSuggestionService> _logger;

    public ProductTagSuggestionService(
        IProductCatalogService catalog,
        IProductTagRepository tags,
        IProductCategoryRepository categories,
        ITagSuggester suggester,
        ILogger<ProductTagSuggestionService> logger)
    {
        _catalog = catalog;
        _tags = tags;
        _categories = categories;
        _suggester = suggester;
        _logger = logger;
    }

    public async Task<ProductTagSuggestions> SuggestAsync(
        Guid productId,
        string? locale,
        CancellationToken cancellationToken)
    {
        var product = await _catalog.GetProductAsync(productId, cancellationToken)
            ?? throw new ProductNotFoundException(productId);

        var current = (product.Tags ?? []).Select(tag => tag.Id).ToHashSet();
        var offered = (await _tags.ListTagsAsync(cancellationToken))
            .Where(tag => !current.Contains(tag.Id))
            .OrderBy(tag => tag.NormalizedName, StringComparer.Ordinal)
            .Select(tag => new TagSuggestionOption(tag.Id, tag.Name))
            .ToList();

        // Nothing to choose from (no tags yet, or the product already has all
        // of them; also a full product): answer without a paid call.
        if (offered.Count == 0 || current.Count >= ProductTagLink.MaximumTagsPerProduct)
        {
            return new ProductTagSuggestions(productId, [], offered.Count);
        }

        if (!_suggester.IsConfigured)
        {
            SuggesterNotConfigured(productId);
            throw new TagSuggestionsUnavailableException("Tag suggestions are not configured on this server.");
        }

        var input = new TagSuggestionProduct(
            product.CanonicalName,
            product.Brand,
            await CategoryPathAsync(product.CategoryId, cancellationToken),
            product.Aliases.Select(alias => alias.RawText).Distinct(StringComparer.OrdinalIgnoreCase).Take(MaximumContextEntries).ToList(),
            product.Aliases.Select(alias => alias.MerchantKey).Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(MaximumContextEntries).ToList());

        var raw = await _suggester.SuggestAsync(input, offered, ParseLanguage(locale), cancellationToken);

        // Never trust the model: keep only tags that were offered, once each.
        var names = offered.ToDictionary(option => option.TagId, option => option.Name);
        var seen = new HashSet<Guid>();
        var suggestions = new List<ProductTagSuggestion>();
        var dropped = 0;
        foreach (var suggestion in raw)
        {
            if (!names.TryGetValue(suggestion.TagId, out var name) || !seen.Add(suggestion.TagId))
            {
                dropped++;
                continue;
            }

            if (suggestions.Count < MaximumSuggestions)
            {
                suggestions.Add(new ProductTagSuggestion(suggestion.TagId, name, CleanReason(suggestion.Reason)));
            }
        }

        if (dropped > 0)
        {
            UnknownOrDuplicateTagsIgnored(productId, dropped);
        }

        return new ProductTagSuggestions(productId, suggestions, offered.Count);
    }

    internal static TagSuggestionLanguage ParseLanguage(string? locale) =>
        locale is not null && locale.StartsWith("en", StringComparison.OrdinalIgnoreCase)
            ? TagSuggestionLanguage.English
            : TagSuggestionLanguage.Russian;

    private static string CleanReason(string? reason)
    {
        var clean = string.Join(' ', (reason ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return clean.Length <= MaximumReasonLength ? clean : clean[..(MaximumReasonLength - 1)].TrimEnd() + "…";
    }

    private async Task<string?> CategoryPathAsync(Guid? categoryId, CancellationToken cancellationToken)
    {
        if (categoryId is null)
        {
            return null;
        }

        var categories = await _categories.ListCategoriesAsync(cancellationToken);
        var category = categories.FirstOrDefault(entry => entry.Id == categoryId);
        if (category is null)
        {
            return null;
        }

        var parent = category.ParentId is { } parentId ? categories.FirstOrDefault(entry => entry.Id == parentId) : null;
        return parent is null ? category.Name : $"{parent.Name} / {category.Name}";
    }

    [LoggerMessage(EventId = 3402, Level = LogLevel.Warning,
        Message = "Tag suggestions for product {ProductId} are unavailable: the suggester is switched off or has no model/credential configured")]
    private partial void SuggesterNotConfigured(Guid productId);

    [LoggerMessage(EventId = 3401, Level = LogLevel.Information,
        Message = "Tag suggestions for product {ProductId}: ignored {Count} unknown or repeated tags from the model")]
    private partial void UnknownOrDuplicateTagsIgnored(Guid productId, int count);
}
