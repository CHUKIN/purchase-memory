using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using PurchaseMemory.Api.Endpoints;
using PurchaseMemory.Application.Products.Categories;
using PurchaseMemory.Application.Products.Tags;
using PurchaseMemory.Application.ReviewQueue;

namespace PurchaseMemory.Api.Mcp;

/// <summary>Read-only view of the «Нужно проверить» queue.</summary>
[McpServerToolType]
public sealed class ReviewQueueTools(IReviewQueueService service)
{
    [McpServerTool(
        Name = "list_review_queue",
        Title = "List items that need review",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "What in the stored purchases still needs a person: failed or unsaved receipts, purchases whose lines " +
        "do not add up to the receipt total or lack prices, unmatched receipt lines, and products with an " +
        "unconfirmed (AI-suggested) or missing category. Grouped by kind with total counts. Optional filters, " +
        "combinable: categoryId (from list_product_categories; a group includes its subcategories, an " +
        "AI-suggested category counts) or uncategorized=true, and tagIds (ids from list_tags; a product needs " +
        "all of them). A filter keeps only items about matching products: the product itself for category " +
        "items, any linked line for total and price items. Unsaved or failed receipts and unmatched lines have " +
        "no product, so any filter leaves them out; totals are then the filtered counts.")]
    public async Task<ReviewQueueResponse> ListReviewQueueAsync(
        [Description("Maximum items returned per kind, 1-100.")] int limitPerKind = ReviewQueueService.DefaultLimitPerKind,
        [Description("Category id from list_product_categories (a group includes its subcategories), or null for any.")]
        Guid? categoryId = null,
        [Description("Only items about products without a category. Cannot be combined with categoryId.")]
        bool uncategorized = false,
        [Description("Tag ids from list_tags, or null for the whole queue. Several ids mean a product needs all of them.")]
        Guid[]? tagIds = null,
        CancellationToken cancellationToken = default)
    {
        var limit = Math.Clamp(limitPerKind, 1, ReviewQueueService.MaximumLimitPerKind);
        try
        {
            return ReviewQueueResponse.From(await service.GetAsync(
                limit,
                new ReviewQueueFilter(categoryId, uncategorized, tagIds),
                cancellationToken));
        }
        catch (ProductCategoryNotFoundException exception)
        {
            // Safe to expose: the client can fetch list_product_categories and retry.
            throw new McpException(exception.Message, exception);
        }
        catch (ProductTagNotFoundException exception)
        {
            // Safe to expose: the client can fetch list_tags and retry.
            throw new McpException(exception.Message, exception);
        }
        catch (ArgumentException exception)
        {
            throw new McpException(exception.Message, exception);
        }
    }
}
