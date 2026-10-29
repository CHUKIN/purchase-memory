namespace PurchaseMemory.Application.Products.Categories;

/// <summary>
/// Suggests a category, a brand and human names (Polish + Russian) for catalog
/// products, all in one request per batch. Implementations talk to a model;
/// they must only return category identifiers they were offered, but callers
/// validate that again and ignore anything else.
/// </summary>
public interface IProductCategorizer
{
    /// <summary>Whether a call can be made at all (model and credential configured).</summary>
    bool IsConfigured { get; }

    /// <summary>Model name recorded on the audit trail, for example <c>gpt-6-luna</c>.</summary>
    string ModelName { get; }

    /// <summary>Products sent in one request.</summary>
    int MaximumBatchSize { get; }

    Task<ProductCategorizationResult> SuggestAsync(
        IReadOnlyList<ProductToCategorize> products,
        IReadOnlyList<CategoryOption> categories,
        CancellationToken cancellationToken);
}

/// <param name="Name">The receipt text (<c>CanonicalName</c>).</param>
/// <param name="Brand">A brand the product already has, as a hint for naming; <c>null</c> when unknown.</param>
/// <param name="ConfirmedNamePl">A Polish name a person confirmed, so the Russian one translates it.</param>
/// <param name="ConfirmedNameRu">A Russian name a person confirmed, so the Polish one matches it.</param>
public sealed record ProductToCategorize(
    Guid ProductId,
    string Name,
    string? Brand = null,
    string? ConfirmedNamePl = null,
    string? ConfirmedNameRu = null);

/// <summary>A category offered to the model, with its full path for context («Молочное и яйца / Сыр»).</summary>
public sealed record CategoryOption(Guid CategoryId, string Path);

/// <summary>
/// What the model said about one product; any part may be <c>null</c> («not
/// sure»). <paramref name="NamePl"/> is the shelf name, <paramref name="NameRu"/>
/// its Russian translation.
/// </summary>
public sealed record ProductCategorySuggestion(
    Guid ProductId,
    Guid? CategoryId,
    string? Brand,
    string? NamePl = null,
    string? NameRu = null);

public sealed record ProductCategorizationResult(
    IReadOnlyList<ProductCategorySuggestion> Suggestions,
    int? InputTokens,
    int? OutputTokens);

/// <summary>Raised by a categorizer for any failure a caller should log and move past.</summary>
public sealed class ProductCategorizationException : Exception
{
    public ProductCategorizationException(string message)
        : base(message)
    {
    }

    public ProductCategorizationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Hands freshly linked products to background categorization. Writing must
/// never throw or wait: it runs on the receipt save path.
/// </summary>
public interface IProductCategorizationDispatcher
{
    void Enqueue(IReadOnlyCollection<Guid> productIds);
}

/// <summary>Default when no background worker is running (tests, tools).</summary>
public sealed class NullProductCategorizationDispatcher : IProductCategorizationDispatcher
{
    public void Enqueue(IReadOnlyCollection<Guid> productIds)
    {
    }
}
