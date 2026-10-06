using System.ComponentModel;
using ModelContextProtocol.Server;
using PurchaseMemory.Application.Analytics;

namespace PurchaseMemory.Api.Mcp;

/// <summary>
/// Read-only analytics for MCP clients: the client (itself a model) writes the
/// plan, and the server validates and runs it. No model is called here, so the
/// tool has no cost and no rate limit beyond the endpoint's authentication.
/// </summary>
[McpServerToolType]
public sealed class AnalyticsTools
{
    private readonly IAnalyticsService _analytics;

    public AnalyticsTools(IAnalyticsService analytics)
    {
        _analytics = analytics;
    }

    [McpServerTool(
        Name = "run_analytics_plan",
        Title = "Run analytics plan",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Compute figures from the purchase history with a typed plan. metric: spend (paid, discounts included), quantity, " +
        "purchaseCount, averageUnitPrice/minimumUnitPrice/maximumUnitPrice (shelf price before discounts), exists, top. " +
        "productIds, categoryIds (a group includes its subcategories; ids from list_product_categories) and nameTerms " +
        "(receipt wording, e.g. mleko) are alternatives; tagIds (ids from list_tags; a line needs all of them), brands, merchants, " +
        "from/to (inclusive yyyy-MM-dd local days) and discount (any, discountedOnly, notDiscounted) narrow further. " +
        "groupBy: none, month, week, merchant, category, product, brand, tag (a line counts under each tag of its product, " +
        "so tag groups can sum to more than the total — coverage.multiTaggedLines > 0 means they do; untagged lines form a group with no label). " +
        "Unknown ids and names are dropped and listed in notes; read coverage before trusting a figure.")]
    public async Task<AnalyticsAnswer> RunAnalyticsPlanAsync(
        [Description("The plan to run.")] AnalyticsPlan plan,
        [Description("ru or en for the conclusion sentence.")] string locale = "ru",
        CancellationToken cancellationToken = default)
    {
        return await _analytics.RunAsync(plan, locale, cancellationToken);
    }
}
