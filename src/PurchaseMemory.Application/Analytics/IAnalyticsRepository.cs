namespace PurchaseMemory.Application.Analytics;

/// <summary>Read-only access to what analytics needs from the database.</summary>
public interface IAnalyticsRepository
{
    Task<AnalyticsVocabulary> GetVocabularyAsync(CancellationToken cancellationToken);

    Task<IReadOnlySet<Guid>> FindExistingProductIdsAsync(IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken);

    /// <summary>
    /// Purchases (with every line) whose moment falls in the range, newest
    /// first, at most <paramref name="maximumPurchases"/> of them.
    /// </summary>
    Task<IReadOnlyList<AnalyticsPurchaseFact>> LoadPurchasesAsync(
        DateTimeOffset? fromInclusive,
        DateTimeOffset? toExclusive,
        int maximumPurchases,
        CancellationToken cancellationToken);
}

/// <param name="PurchasedAt">The receipt date, or when it was recorded if the receipt had none.</param>
public sealed record AnalyticsPurchaseFact(
    Guid PurchaseId,
    Guid? SourceReceiptId,
    DateTimeOffset PurchasedAt,
    bool HasReceiptDate,
    string? MerchantName,
    string? Currency,
    IReadOnlyList<AnalyticsLineFact> Lines);

/// <param name="ProductName">Canonical name of the linked product.</param>
/// <param name="ProductSearchText">Normalized product name and aliases, space separated, for term matching.</param>
/// <param name="TagIds">Tags of the linked product; empty when it has none or the line is not linked.</param>
public sealed record AnalyticsLineFact(
    int Position,
    string Name,
    decimal? Quantity,
    string? Unit,
    decimal? UnitPrice,
    decimal? LineTotal,
    bool IsDiscount,
    int? DiscountedItemPosition,
    Guid? ProductId,
    string? ProductName,
    string? ProductSearchText,
    Guid? CategoryId,
    string? Brand,
    IReadOnlyList<Guid>? TagIds = null)
{
    public IReadOnlyList<Guid> TagIds { get; init; } = TagIds ?? [];
}
