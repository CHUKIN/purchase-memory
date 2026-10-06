using System.ComponentModel;
using ModelContextProtocol.Server;
using PurchaseMemory.Api.Endpoints;
using PurchaseMemory.Application.Impressions;

namespace PurchaseMemory.Api.Mcp;

/// <summary>Read-only view of household impressions. Writing stays in the app, where the author is known.</summary>
[McpServerToolType]
public sealed class ImpressionTools(IProductImpressionService service)
{
    [McpServerTool(
        Name = "list_product_impressions",
        Title = "List impressions of a product",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Household members' impressions of one catalog product, newest first: rating (dislike, neutral, like), " +
        "optional note, author, and the purchase each one is about (store, date, price). Withdrawn impressions " +
        "are not listed. isMine is always false in this tool.")]
    public async Task<IReadOnlyList<ImpressionResponse>> ListProductImpressionsAsync(
        [Description("Catalog product id (from search_products).")] Guid productId,
        CancellationToken cancellationToken = default)
    {
        var views = await service.ListForProductAsync(productId, viewerUserId: string.Empty, cancellationToken);
        return views.Select(ImpressionResponse.From).ToList();
    }
}
