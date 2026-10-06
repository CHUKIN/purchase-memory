using System.Text.Json;
using System.Text.Json.Serialization;

namespace PurchaseMemory.Application.Analytics;

/// <summary>Plan enums cross every wire (REST, MCP, the model) as camelCase strings, never numbers.</summary>
public sealed class CamelCaseEnumConverter<TEnum>() : JsonStringEnumConverter<TEnum>(JsonNamingPolicy.CamelCase, allowIntegerValues: false)
    where TEnum : struct, Enum;

/// <summary>What the answer measures. The model picks one; the numbers always come from the database.</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<AnalyticsMetric>))]
public enum AnalyticsMetric
{
    /// <summary>Money actually paid: product lines plus their (negative) discount lines.</summary>
    Spend,

    /// <summary>Bought quantity per normalized unit; lines without a quantity count as one.</summary>
    Quantity,

    /// <summary>Distinct receipts that contain a matching line.</summary>
    PurchaseCount,

    /// <summary>Average price per unit of the product line, before discounts.</summary>
    AverageUnitPrice,

    MinimumUnitPrice,

    MaximumUnitPrice,

    /// <summary>«Did we buy …?» — yes/no with how often and when last.</summary>
    Exists,

    /// <summary>The largest groups by spend; the grouping defaults to products.</summary>
    Top
}

[JsonConverter(typeof(CamelCaseEnumConverter<AnalyticsGroupBy>))]
public enum AnalyticsGroupBy
{
    None,
    Month,
    Week,
    Merchant,
    Category,
    Product,
    Brand,

    /// <summary>
    /// One bar per tag. A line is counted under every tag its product carries,
    /// so the bars may add up to more than the total; untagged lines form their own bar.
    /// </summary>
    Tag
}

[JsonConverter(typeof(CamelCaseEnumConverter<AnalyticsDiscountFilter>))]
public enum AnalyticsDiscountFilter
{
    Any,

    /// <summary>Only product lines that have a linked discount line.</summary>
    DiscountedOnly,

    /// <summary>Only product lines without a linked discount line.</summary>
    NotDiscounted
}

/// <summary>
/// A validated, typed query: the only thing the model is allowed to produce
/// (after validation) and the only thing the executor accepts.
/// <para>
/// Product selectors — <see cref="ProductIds"/>, <see cref="CategoryIds"/> and
/// <see cref="NameTerms"/> — are alternatives (a line matches if any of them
/// matches), because the same thing is often both a category and a word on the
/// receipt, and an uncategorized product would otherwise be lost. Tags,
/// brands, merchants, the period and the discount filter narrow the result
/// further.
/// </para>
/// </summary>
public sealed record AnalyticsPlan
{
    public const int MaximumListLength = 20;
    public const int MaximumTermLength = 60;
    public const int DefaultTopLimit = 5;
    public const int MaximumTopLimit = 20;

    public AnalyticsMetric Metric { get; init; } = AnalyticsMetric.Spend;

    public IReadOnlyList<Guid> ProductIds { get; init; } = [];

    /// <summary>A group includes its subcategories.</summary>
    public IReadOnlyList<Guid> CategoryIds { get; init; } = [];

    /// <summary>Receipt wording to look for in item names, product names and aliases.</summary>
    public IReadOnlyList<string> NameTerms { get; init; } = [];

    public IReadOnlyList<string> Brands { get; init; } = [];

    /// <summary>
    /// Household tags; a line matches only if its product carries <em>all</em> of
    /// them — the same rule as the purchases and products filters. One tag is the usual case.
    /// </summary>
    public IReadOnlyList<Guid> TagIds { get; init; } = [];

    public IReadOnlyList<string> Merchants { get; init; } = [];

    /// <summary>Inclusive first local calendar day, or null for «since the beginning».</summary>
    public DateOnly? From { get; init; }

    /// <summary>Inclusive last local calendar day, or null for «until today».</summary>
    public DateOnly? To { get; init; }

    public AnalyticsDiscountFilter Discount { get; init; } = AnalyticsDiscountFilter.Any;

    public AnalyticsGroupBy GroupBy { get; init; } = AnalyticsGroupBy.None;

    /// <summary>Only for <see cref="AnalyticsMetric.Top"/>.</summary>
    public int? Limit { get; init; }

    public bool HasProductSelector => ProductIds.Count > 0 || CategoryIds.Count > 0 || NameTerms.Count > 0;

    public bool HasLineFilter => HasProductSelector || Brands.Count > 0 || TagIds.Count > 0 || Discount != AnalyticsDiscountFilter.Any;
}

/// <summary>
/// Something the server changed or dropped while validating a plan, as a code
/// the answer text turns into words; <see cref="Value"/> is the offending input.
/// </summary>
public sealed record AnalyticsNote(string Code, string? Value = null)
{
    public const string UnknownCategory = "unknownCategory";
    public const string UnknownProduct = "unknownProduct";
    public const string UnknownMerchant = "unknownMerchant";
    public const string UnknownBrand = "unknownBrand";
    public const string UnknownTag = "unknownTag";
    public const string PeriodSwapped = "periodSwapped";
    public const string PeriodOutOfRange = "periodOutOfRange";
    public const string TooManyValues = "tooManyValues";
    public const string EmptyTermDropped = "emptyTermDropped";
    public const string LimitClamped = "limitClamped";
    public const string NotUnderstood = "notUnderstood";
}
