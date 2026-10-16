namespace PurchaseMemory.Domain.Products;

/// <summary>
/// An existing product the matcher weighed when it created a flagged product
/// instead of linking. The list belongs to the flagged product, not to a
/// receipt line, so it survives purchase corrections (which replace line rows)
/// and disappears with the flag once a person decides. The score is kept so a
/// reviewer can see why the decision was uncertain.
/// </summary>
public sealed class ProductReviewCandidate
{
    private ProductReviewCandidate()
    {
        // Required by Entity Framework Core.
    }

    private ProductReviewCandidate(Guid id, Guid productId, int position, Guid candidateProductId, double score)
    {
        Id = id;
        ProductId = productId;
        Position = position;
        CandidateProductId = candidateProductId;
        Score = score;
    }

    public Guid Id { get; private set; }

    /// <summary>The flagged product this candidate list belongs to.</summary>
    public Guid ProductId { get; private set; }

    /// <summary>Zero-based rank by descending score.</summary>
    public int Position { get; private set; }

    /// <summary>The existing product it might really be.</summary>
    public Guid CandidateProductId { get; private set; }

    public double Score { get; private set; }

    public static ProductReviewCandidate Create(Guid productId, int position, Guid candidateProductId, double score)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);

        if (productId == Guid.Empty || candidateProductId == Guid.Empty)
        {
            throw new ArgumentException("Product identifiers cannot be empty.");
        }

        if (productId == candidateProductId)
        {
            throw new ArgumentException("A product cannot be its own candidate.", nameof(candidateProductId));
        }

        if (score is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(score), score, "Score must be between 0 and 1.");
        }

        return new ProductReviewCandidate(Guid.NewGuid(), productId, position, candidateProductId, score);
    }
}
