using PurchaseMemory.Domain.Purchases;

namespace PurchaseMemory.Application.Products;

/// <summary>
/// Links the items of a saved purchase to the catalog. Implementations must be
/// idempotent: matching the same purchase twice may not create a second product
/// or a second alias.
/// </summary>
public interface IProductMatcher
{
    Task<ProductMatchSummary> MatchAsync(PurchaseDraft purchase, CancellationToken cancellationToken);
}

/// <summary>How a single receipt line was resolved.</summary>
public enum ProductMatchOutcome
{
    /// <summary>The item already carried a catalog link, so it was left alone.</summary>
    AlreadyLinked = 0,

    /// <summary>A store-scoped alias matched the normalized text exactly.</summary>
    AliasHit = 1,

    /// <summary>Similarity picked one clearly dominant existing product.</summary>
    AutoMatched = 2,

    /// <summary>Nothing plausible existed, so a new product bootstrapped the catalog.</summary>
    AutoCreated = 3,

    /// <summary>
    /// Ambiguous or grey-zone: the line got a new product of its own carrying
    /// the «проверить» flag and the candidates it might really be.
    /// </summary>
    FlaggedForReview = 4,

    /// <summary>The item name normalized to nothing, so it carries no usable key.</summary>
    Skipped = 5
}

public sealed record ProductMatchSummary(
    int AliasHits,
    int AutoMatched,
    int AutoCreated,
    int FlaggedForReview,
    int AlreadyLinked,
    int Skipped)
{
    public static readonly ProductMatchSummary Empty = new(0, 0, 0, 0, 0, 0);

    public int Total => AliasHits + AutoMatched + AutoCreated + FlaggedForReview + AlreadyLinked + Skipped;

    public ProductMatchSummary Add(ProductMatchOutcome outcome) => outcome switch
    {
        ProductMatchOutcome.AliasHit => this with { AliasHits = AliasHits + 1 },
        ProductMatchOutcome.AutoMatched => this with { AutoMatched = AutoMatched + 1 },
        ProductMatchOutcome.AutoCreated => this with { AutoCreated = AutoCreated + 1 },
        ProductMatchOutcome.FlaggedForReview => this with { FlaggedForReview = FlaggedForReview + 1 },
        ProductMatchOutcome.AlreadyLinked => this with { AlreadyLinked = AlreadyLinked + 1 },
        ProductMatchOutcome.Skipped => this with { Skipped = Skipped + 1 },
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null)
    };

    public ProductMatchSummary Merge(ProductMatchSummary other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return new ProductMatchSummary(
            AliasHits + other.AliasHits,
            AutoMatched + other.AutoMatched,
            AutoCreated + other.AutoCreated,
            FlaggedForReview + other.FlaggedForReview,
            AlreadyLinked + other.AlreadyLinked,
            Skipped + other.Skipped);
    }
}
