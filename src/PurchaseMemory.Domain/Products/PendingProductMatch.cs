namespace PurchaseMemory.Domain.Products;

/// <summary>
/// An item the deterministic matcher refused to link: either several products
/// scored plausibly, or the single best one landed in the grey zone. The record
/// is what the future «needs attention» queue reads; this stage only writes it.
/// </summary>
public sealed class PendingProductMatch
{
    private readonly List<PendingProductMatchCandidate> _candidates = [];

    private PendingProductMatch()
    {
        // Required by Entity Framework Core.
    }

    private PendingProductMatch(
        Guid id,
        Guid purchaseItemId,
        string merchantKey,
        string rawItemName,
        string normalizedItemName,
        DateTimeOffset raisedAt)
    {
        Id = id;
        PurchaseItemId = purchaseItemId;
        MerchantKey = merchantKey;
        RawItemName = rawItemName;
        NormalizedItemName = normalizedItemName;
        RaisedAt = raisedAt;
    }

    public Guid Id { get; private set; }

    public Guid PurchaseItemId { get; private set; }

    public string MerchantKey { get; private set; } = ProductAlias.UnknownMerchantKey;

    public string RawItemName { get; private set; } = string.Empty;

    public string NormalizedItemName { get; private set; } = string.Empty;

    public DateTimeOffset RaisedAt { get; private set; }

    public IReadOnlyCollection<PendingProductMatchCandidate> Candidates => _candidates;

    public static PendingProductMatch Create(
        Guid purchaseItemId,
        string? merchantKey,
        string rawItemName,
        string normalizedItemName,
        IEnumerable<PendingProductMatchCandidate> candidates,
        DateTimeOffset raisedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawItemName);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedItemName);
        ArgumentNullException.ThrowIfNull(candidates);

        if (purchaseItemId == Guid.Empty)
        {
            throw new ArgumentException("Purchase item identifier cannot be empty.", nameof(purchaseItemId));
        }

        var pending = new PendingProductMatch(
            Guid.NewGuid(),
            purchaseItemId,
            merchantKey ?? ProductAlias.UnknownMerchantKey,
            Truncate(rawItemName.Trim()),
            Truncate(normalizedItemName),
            raisedAt);

        pending._candidates.AddRange(candidates.OrderBy(candidate => candidate.Position));
        if (pending._candidates.Count == 0)
        {
            throw new ArgumentException(
                "A pending match must list the candidates that were considered.",
                nameof(candidates));
        }

        return pending;
    }

    private static string Truncate(string value)
    {
        return value.Length <= ProductAlias.MaximumTextLength
            ? value
            : value[..ProductAlias.MaximumTextLength];
    }
}

/// <summary>
/// One product the matcher weighed, with the similarity score it produced. The
/// score is stored so a later reviewer can see why the decision was uncertain.
/// </summary>
public sealed class PendingProductMatchCandidate
{
    private PendingProductMatchCandidate()
    {
        // Required by Entity Framework Core.
    }

    private PendingProductMatchCandidate(Guid id, int position, Guid productId, double score)
    {
        Id = id;
        Position = position;
        ProductId = productId;
        Score = score;
    }

    public Guid Id { get; private set; }

    /// <summary>Zero-based rank by descending score.</summary>
    public int Position { get; private set; }

    public Guid ProductId { get; private set; }

    public double Score { get; private set; }

    public static PendingProductMatchCandidate Create(int position, Guid productId, double score)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);

        if (productId == Guid.Empty)
        {
            throw new ArgumentException("Product identifier cannot be empty.", nameof(productId));
        }

        if (score is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(score), score, "Score must be between 0 and 1.");
        }

        return new PendingProductMatchCandidate(Guid.NewGuid(), position, productId, score);
    }
}
