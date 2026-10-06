namespace PurchaseMemory.Application.Analytics;

public sealed record AnalyticsCategory(Guid Id, Guid? ParentId, string Name, string? SystemKey);

/// <summary>A household tag as analytics sees it: the id plans refer to and the name people read.</summary>
public sealed record AnalyticsTag(Guid Id, string Name);

/// <summary>What exists in the household's data: the only ids and names a plan may refer to.</summary>
public sealed record AnalyticsVocabulary(
    IReadOnlyList<AnalyticsCategory> Categories,
    IReadOnlyList<string> Merchants,
    IReadOnlyList<string> Brands,
    IReadOnlyList<AnalyticsTag>? Tags = null)
{
    /// <summary>Every household tag, most used first (then alphabetically); the planner offers the model only the first ones.</summary>
    public IReadOnlyList<AnalyticsTag> Tags { get; init; } = Tags ?? [];
}
