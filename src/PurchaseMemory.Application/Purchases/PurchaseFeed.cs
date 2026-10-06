namespace PurchaseMemory.Application.Purchases;

/// <summary>
/// Review status of a saved purchase. It is derived from
/// <see cref="PurchaseDraftView.Issues"/> and is not stored in the database,
/// because issues are recomputed from the current snapshot on every read.
/// </summary>
public enum PurchaseReviewStatus
{
    All = 0,
    NeedsAttention = 1,
    Reviewed = 2
}

/// <summary>
/// Chronological feed request. All filters combine with AND semantics.
/// </summary>
public sealed record PurchaseFeedQuery
{
    public const int DefaultPageSize = 20;
    public const int MaximumPageSize = 100;
    public const int MaximumQueryLength = 200;

    public string? Query { get; init; }

    /// <summary>Inclusive lower bound applied to <c>PurchasedAt</c>.</summary>
    public DateTimeOffset? From { get; init; }

    /// <summary>Inclusive upper bound applied to <c>PurchasedAt</c>.</summary>
    public DateTimeOffset? To { get; init; }

    public PurchaseReviewStatus Status { get; init; } = PurchaseReviewStatus.All;

    /// <summary>
    /// Purchases with at least one line linked to a product in this category
    /// or, for a group, in any of its subcategories.
    /// </summary>
    public Guid? CategoryId { get; init; }

    /// <summary>
    /// Purchases with at least one line linked to a product without a
    /// category. Lines not linked to any product do not count.
    /// </summary>
    public bool Uncategorized { get; init; }

    /// <summary>Purchases with at least one line whose product brand contains this text.</summary>
    public string? Brand { get; init; }

    /// <summary>Purchases with at least one line whose product carries every one of these tags.</summary>
    public IReadOnlyCollection<Guid>? TagIds { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = DefaultPageSize;
}

public sealed record PurchaseFeedPage(
    IReadOnlyList<PurchaseDraftView> Items,
    int Page,
    int PageSize,
    int TotalCount,
    bool HasMore,
    bool Truncated);
