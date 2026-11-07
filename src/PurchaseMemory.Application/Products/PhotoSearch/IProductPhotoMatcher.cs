using PurchaseMemory.Application.Products.PhotoNames;

namespace PurchaseMemory.Application.Products.PhotoSearch;

/// <summary>
/// «Найти по фото»: tells which of the household's own products a photo most
/// likely shows (a package, or loose vegetables, fruit, bread). The
/// implementation calls a paid vision model with the photo and a compact
/// text list of candidates; the photo is sent once and never stored or logged.
/// </summary>
/// <remarks>
/// Candidates are text only for now: the catalog has no product photos yet.
/// When it has, a candidate can carry its main-photo thumbnail and the
/// implementation can send those as extra images next to the codes.
/// </remarks>
public interface IProductPhotoMatcher
{
    /// <summary>The feature switch. Off: the endpoint says «disabled» and the UI hides the button.</summary>
    bool IsEnabled { get; }

    /// <summary>Enabled and has a model and a credential; otherwise nothing is called.</summary>
    bool IsConfigured { get; }

    /// <summary>The model id, for logs and the measurement tool.</summary>
    string ModelName { get; }

    /// <summary>How many candidates one call may carry (the prompt's token budget).</summary>
    int MaximumCandidates { get; }

    /// <summary>
    /// Fails only with <see cref="ProductPhotoSearchUnavailableException"/>
    /// (not configured, provider error, refusal, malformed answer). The
    /// matches only ever name products from <paramref name="candidates"/>.
    /// </summary>
    Task<ProductPhotoMatchReading> MatchAsync(
        ProductPhotoImage image,
        IReadOnlyList<ProductPhotoCandidate> candidates,
        CancellationToken cancellationToken);
}

/// <summary>What the model is told about one of the household's products.</summary>
public sealed record ProductPhotoCandidate(
    Guid ProductId,
    string ReceiptName,
    string? NamePl,
    string? NameRu,
    string? Brand,
    string? Category);

public enum ProductPhotoMatchConfidence
{
    Low,
    Medium,
    High
}

public sealed record ProductPhotoMatch(Guid ProductId, ProductPhotoMatchConfidence Confidence);

/// <summary>
/// Best match first, at most three. <see cref="RejectedCount"/> counts the
/// answers that named no candidate (an unknown code, a repeat, a fourth one)
/// and were dropped.
/// </summary>
public sealed record ProductPhotoMatchReading(
    IReadOnlyList<ProductPhotoMatch> Matches,
    int RejectedCount,
    long? InputTokens,
    long? OutputTokens,
    decimal? CostUsd);

/// <summary>The search cannot run right now; the cause is in the log.</summary>
public sealed class ProductPhotoSearchUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>The feature switch is off on this server.</summary>
public sealed class ProductPhotoSearchDisabledException()
    : Exception("Finding products by photo is switched off on this server.");

public static class ProductPhotoSearchRules
{
    /// <summary>The answer is a short choice, never a ranking of the whole list.</summary>
    public const int MaximumMatches = 3;
}
