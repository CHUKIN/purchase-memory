using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PurchaseMemory.Application.Products.Media;
using PurchaseMemory.Application.Products.PhotoNames;

namespace PurchaseMemory.Application.Products.PhotoSearch;

/// <summary>Reads the candidates «Найти по фото» may offer.</summary>
public interface IProductPhotoCandidateRepository
{
    /// <summary>
    /// At most <paramref name="maximumCandidates"/> products of the catalog,
    /// the most recently bought first, then the most often bought, then
    /// products never bought (created from a barcode or by hand), newest first.
    /// </summary>
    Task<IReadOnlyList<ProductPhotoCandidateRow>> ListCandidatesAsync(
        int maximumCandidates,
        CancellationToken cancellationToken);
}

/// <param name="LastPurchasedAt">Date of the latest purchase (receipt date, else when it was saved); null when never bought.</param>
/// <param name="PurchaseCount">Distinct purchases (shopping trips) the product is on.</param>
public sealed record ProductPhotoCandidateRow(
    ProductPhotoCandidate Candidate,
    DateTimeOffset? LastPurchasedAt,
    int PurchaseCount);

public interface IProductPhotoSearchService
{
    /// <summary>The feature switch alone.</summary>
    bool IsEnabled { get; }

    /// <summary>Whether the button should show: switched on and configured.</summary>
    bool IsAvailable { get; }

    /// <exception cref="ProductPhotoSearchDisabledException">The switch is off.</exception>
    /// <exception cref="ProductPhotoSearchUnavailableException">No model/credential, or the model failed.</exception>
    Task<ProductPhotoSearchResult> FindAsync(ProductPhotoImage image, CancellationToken cancellationToken);
}

/// <summary>One offered product with what the result sheet shows before «Это он».</summary>
public sealed record ProductPhotoSearchHit(
    Guid ProductId,
    string CanonicalName,
    string? NamePl,
    string? NameRu,
    string? Brand,
    string? Category,
    Guid? MainPhotoId,
    int PurchaseCount,
    DateTimeOffset? LastPurchasedAt,
    ProductPhotoMatchConfidence Confidence);

/// <param name="CandidateCount">How many products the model chose from (the list is capped).</param>
public sealed record ProductPhotoSearchResult(IReadOnlyList<ProductPhotoSearchHit> Matches, int CandidateCount);

public sealed partial class ProductPhotoSearchService : IProductPhotoSearchService
{
    private readonly IProductPhotoCandidateRepository _candidates;
    private readonly IProductMediaRepository _media;
    private readonly IProductPhotoMatcher _matcher;
    private readonly ILogger<ProductPhotoSearchService> _logger;

    public ProductPhotoSearchService(
        IProductPhotoCandidateRepository candidates,
        IProductMediaRepository media,
        IProductPhotoMatcher matcher,
        ILogger<ProductPhotoSearchService> logger)
    {
        _candidates = candidates;
        _media = media;
        _matcher = matcher;
        _logger = logger;
    }

    public bool IsEnabled => _matcher.IsEnabled;

    public bool IsAvailable => _matcher.IsEnabled && _matcher.IsConfigured;

    public async Task<ProductPhotoSearchResult> FindAsync(ProductPhotoImage image, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (!_matcher.IsEnabled)
        {
            throw new ProductPhotoSearchDisabledException();
        }

        if (!_matcher.IsConfigured)
        {
            MatcherNotConfigured();
            throw new ProductPhotoSearchUnavailableException("Finding products by photo is not configured.");
        }

        var stopwatch = Stopwatch.StartNew();
        var rows = await _candidates.ListCandidatesAsync(Math.Max(1, _matcher.MaximumCandidates), cancellationToken);
        if (rows.Count == 0)
        {
            // An empty catalog: nothing to choose from, no paid call.
            return new ProductPhotoSearchResult([], 0);
        }

        var reading = await _matcher.MatchAsync(image, rows.Select(row => row.Candidate).ToList(), cancellationToken);

        // The matcher already keeps to the list; this second check keeps any
        // implementation from offering a product that was not a candidate.
        var byId = rows.ToDictionary(row => row.Candidate.ProductId);
        var accepted = reading.Matches
            .Where(match => byId.ContainsKey(match.ProductId))
            .DistinctBy(match => match.ProductId)
            .Take(ProductPhotoSearchRules.MaximumMatches)
            .ToList();
        var rejected = reading.RejectedCount + (reading.Matches.Count - accepted.Count);
        var photos = accepted.Count == 0
            ? new Dictionary<Guid, Guid>()
            : await _media.MainPhotoIdsAsync(accepted.Select(match => match.ProductId).ToList(), cancellationToken);

        var hits = accepted.Select(match =>
        {
            var row = byId[match.ProductId];
            var candidate = row.Candidate;
            return new ProductPhotoSearchHit(
                candidate.ProductId,
                candidate.ReceiptName,
                candidate.NamePl,
                candidate.NameRu,
                candidate.Brand,
                candidate.Category,
                photos.TryGetValue(candidate.ProductId, out var photoId) ? photoId : null,
                row.PurchaseCount,
                row.LastPurchasedAt,
                match.Confidence);
        }).ToList();

        stopwatch.Stop();
        Searched(rows.Count, hits.Count, rejected, stopwatch.ElapsedMilliseconds, _matcher.ModelName,
            reading.InputTokens, reading.OutputTokens, reading.CostUsd);
        return new ProductPhotoSearchResult(hits, rows.Count);
    }

    [LoggerMessage(EventId = 3441, Level = LogLevel.Warning,
        Message = "Find by photo is unavailable: no model or credential is configured")]
    private partial void MatcherNotConfigured();

    // Counts, timing and usage only: never the photo, never product names.
    [LoggerMessage(EventId = 3442, Level = LogLevel.Information,
        Message = "Find by photo: {CandidateCount} candidates, {MatchCount} matches, {RejectedCount} rejected answers in {DurationMs} ms with model {Model}; input tokens {InputTokens}; output tokens {OutputTokens}; estimated cost USD {CostUsd}")]
    private partial void Searched(
        int candidateCount,
        int matchCount,
        int rejectedCount,
        long durationMs,
        string model,
        long? inputTokens,
        long? outputTokens,
        decimal? costUsd);
}
