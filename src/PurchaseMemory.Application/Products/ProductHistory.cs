using PurchaseMemory.Application.Products.Tags;

namespace PurchaseMemory.Application.Products;

/// <summary>
/// One purchased line linked to a product, as the price history needs it. The
/// repository returns these raw; every judgement about them is made by
/// <see cref="ProductPriceHistoryCalculator"/> so the arithmetic stays testable
/// without a database.
/// </summary>
public sealed record ProductPurchaseFact(
    Guid PurchaseId,
    // When the purchase happened; `CreatedAt` stands in when the receipt had no date.
    DateTimeOffset OccurredAt,
    string? MerchantName,
    string? Currency,
    string RawItemName,
    decimal? Quantity,
    string? Unit,
    decimal? UnitPrice,
    decimal? LineTotal);

/// <summary>One dot on the price chart: what a single unit cost on one receipt.</summary>
public sealed record ProductPricePoint(
    DateTimeOffset PurchasedAt,
    string? MerchantName,
    decimal UnitPrice,
    Guid PurchaseId);

/// <summary>
/// Prices that are actually comparable with each other: same currency, same unit
/// of measure. Two series never mix, because «12 zł per kilogram» and «12 zł per
/// piece» are different numbers wearing the same shape.
/// </summary>
public sealed record ProductPriceSeries(
    // ISO 4217 code, or null when no receipt in the series stated one.
    string? Currency,
    // Canonical unit token from ProductUnit, for example `szt` or `kg`.
    string Unit,
    IReadOnlyList<ProductPricePoint> Points,
    decimal MinimumPrice,
    decimal AveragePrice,
    ProductPricePoint LatestPoint)
{
    public decimal LatestPrice => LatestPoint.UnitPrice;
}

/// <summary>
/// How often the product is bought. Only stated once there are two purchases to
/// measure between; a single purchase has no rhythm to report.
/// </summary>
public sealed record ProductPurchaseFrequency(
    DateTimeOffset FirstPurchasedAt,
    DateTimeOffset LastPurchasedAt,
    int PurchaseCount,
    double? AverageDaysBetweenPurchases);

/// <summary>Receipt wording that has been linked to the product, by merchant.</summary>
public sealed record ProductAliasView(string MerchantKey, string RawText);

/// <summary>Everything the product page shows about one catalog product.</summary>
public sealed record ProductDetail(
    Guid ProductId,
    string CanonicalName,
    string NormalizedName,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    // Distinct purchases, matching what the catalog and the frequency block say.
    int PurchaseCount,
    // Receipt lines behind those purchases; always at least PurchaseCount.
    int LinkedItemCount,
    // Comparable price series, the one with the most evidence first. The page
    // leads with that series and offers the rest beside it.
    IReadOnlyList<ProductPriceSeries> PriceSeries,
    // Linked lines that carried no usable price at all. Surfaced so the page can
    // admit that the history it draws is incomplete instead of hiding the gap.
    int UnpricedItemCount,
    ProductPurchaseFrequency? Frequency,
    IReadOnlyList<ProductAliasView> Aliases,
    Guid? CategoryId = null,
    PurchaseMemory.Domain.Products.ProductAttributeSource? CategorySource = null,
    string? Brand = null,
    PurchaseMemory.Domain.Products.ProductAttributeSource? BrandSource = null,
    IReadOnlyList<ProductTagSummary>? Tags = null,
    PurchaseMemory.Domain.Products.ProductOrigin Origin = PurchaseMemory.Domain.Products.ProductOrigin.Receipt,
    bool NeedsReview = false,
    PurchaseMemory.Domain.Products.ProductReviewReason? ReviewReason = null,
    // Flagged products only: what the product might really be, best first.
    IReadOnlyList<ProductReviewCandidateView>? ReviewCandidates = null);

/// <summary>
/// An existing product a flagged product might really be, with what a person
/// needs to recognise it: the name, brand, category and how often it was bought.
/// </summary>
public sealed record ProductReviewCandidateView(
    Guid ProductId,
    string Name,
    string? Brand,
    Guid? CategoryId,
    int PurchaseCount,
    double Score);
