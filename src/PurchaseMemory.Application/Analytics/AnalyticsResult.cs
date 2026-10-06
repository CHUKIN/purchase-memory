using System.Text.Json.Serialization;

namespace PurchaseMemory.Application.Analytics;

[JsonConverter(typeof(CamelCaseEnumConverter<AnalyticsKpiKind>))]
public enum AnalyticsKpiKind
{
    Money,
    Count,
    Quantity,
    Date,
    Boolean
}

/// <summary>One headline number. <see cref="Code"/> names it for the UI («spend», «averagePrice»…).</summary>
public sealed record AnalyticsKpi(
    string Code,
    AnalyticsKpiKind Kind,
    decimal? Value,
    string? Unit = null,
    DateOnly? Date = null);

/// <summary>
/// One bar of the chart. <see cref="Key"/> is stable (a month «2026-08», a
/// week's Monday, an id or a normalized name); <see cref="Label"/> is the
/// stored name, or null for «unknown store», «no category», «no brand», «no tag».
/// In a breakdown by tag a line appears under each of its product's tags, so
/// the bars may add up to more than the total.
/// </summary>
public sealed record AnalyticsGroup(
    string Key,
    string? Label,
    decimal Value,
    int LineCount,
    Guid? CategoryId = null,
    string? CategorySystemKey = null,
    Guid? ProductId = null,
    Guid? TagId = null);

public sealed record AnalyticsEvidenceItem(
    string Name,
    Guid? ProductId,
    decimal? LineTotal,
    decimal DiscountTotal);

/// <param name="Amount">What the matching lines cost on this receipt, discounts included.</param>
public sealed record AnalyticsEvidencePurchase(
    Guid PurchaseId,
    Guid? SourceReceiptId,
    DateTimeOffset PurchasedAt,
    string? MerchantName,
    string Currency,
    decimal Amount,
    int MatchedLineCount,
    IReadOnlyList<AnalyticsEvidenceItem> Items);

/// <summary>
/// What the numbers rest on and what they may miss. Every figure is a plain
/// count, so the UI can say it honestly instead of hiding gaps.
/// </summary>
/// <param name="PurchasesInScope">Receipts inside the period and store filter.</param>
/// <param name="UncategorizedLinesInScope">Product lines in scope whose product has no category — a category filter cannot see them.</param>
/// <param name="UnlinkedLinesInScope">Product lines in scope not linked to any catalog product.</param>
/// <param name="UnpricedMatchedLines">Matching lines without a total; they count as zero money.</param>
/// <param name="UnlinkedDiscountsExcluded">Receipt-level discounts left out because the question is about some items only.</param>
/// <param name="OtherCurrencyLines">Matching lines in a currency other than <see cref="AnalyticsResult.Currency"/>, left out of money figures.</param>
/// <param name="OtherUnitLines">Matching lines in a unit other than the price or quantity unit, left out of that figure.</param>
/// <param name="Truncated">The period held more receipts than one answer reads; the oldest were left out.</param>
/// <param name="AssumedQuantityLines">Quantity answers only: matching lines with no printed quantity, counted as one piece.</param>
/// <param name="MultiTaggedLines">Matching lines whose product carries several tags; a breakdown by tag counts each of them more than once.</param>
public sealed record AnalyticsCoverage(
    int PurchasesInScope,
    int MatchedPurchases,
    int MatchedLines,
    int UncategorizedLinesInScope,
    int UnlinkedLinesInScope,
    int UnpricedMatchedLines,
    int UnlinkedDiscountsExcluded,
    int OtherCurrencyLines,
    int OtherUnitLines,
    int PurchasesWithoutDate,
    bool Truncated,
    int AssumedQuantityLines = 0,
    int MultiTaggedLines = 0);

public sealed record AnalyticsResult(
    AnalyticsMetric Metric,
    AnalyticsGroupBy GroupBy,
    string Currency,
    bool HasData,
    IReadOnlyList<AnalyticsKpi> Kpis,
    IReadOnlyList<AnalyticsGroup> Groups,
    int HiddenGroupCount,
    IReadOnlyList<AnalyticsEvidencePurchase> Evidence,
    int EvidenceTotal,
    AnalyticsCoverage Coverage);
