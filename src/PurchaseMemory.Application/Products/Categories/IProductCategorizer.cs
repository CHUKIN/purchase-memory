namespace PurchaseMemory.Application.Products.Categories;

/// <summary>
/// Suggests a category and brand for catalog products. Implementations talk to
/// a model; they must only return category identifiers they were offered, but
/// callers validate that again and ignore anything else.
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

public sealed record ProductToCategorize(Guid ProductId, string Name);

/// <summary>A category offered to the model, with its full path for context («Молочное и яйца / Сыр»).</summary>
public sealed record CategoryOption(Guid CategoryId, string Path);

public sealed record ProductCategorySuggestion(Guid ProductId, Guid? CategoryId, string? Brand);

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
