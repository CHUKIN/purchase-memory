using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using PurchaseMemory.Api.Logging;
using PurchaseMemory.Application.Purchases;

namespace PurchaseMemory.Api.Mcp;

/// <summary>
/// Thin MCP adapter. Business rules stay in the application/domain layers so a
/// future REST API or UI can reuse the same behavior.
/// </summary>
[McpServerToolType]
public sealed class PurchaseTools
{
    private readonly IPurchaseDraftService _purchaseDraftService;
    private readonly ILogger<PurchaseTools> _logger;

    public PurchaseTools(
        IPurchaseDraftService purchaseDraftService,
        ILogger<PurchaseTools> logger)
    {
        _purchaseDraftService = purchaseDraftService;
        _logger = logger;
    }

    [McpServerTool(
        Name = "create_purchase_draft",
        Title = "Create purchase draft",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Use this only after the user explicitly asks to save a receipt or purchase. " +
        "Read the attached image first, preserve original item names, pass unreadable values as null, " +
        "and never invent products or prices. The server rejects matching duplicates; override that protection " +
        "only after the user confirms they are separate transactions. Returns the stored draft and its purchaseId.")]
    public async Task<PurchaseDraftView> CreatePurchaseDraftAsync(
        [Description("Complete data extracted from one receipt.")]
        CreatePurchaseDraftRequest request,
        CancellationToken cancellationToken)
    {
        return await ExecuteApplicationOperationAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(request.Items);

            PurchaseMemoryLogMessages.CreatingPurchaseDraft(
                _logger,
                request.MerchantName ?? "<unknown>",
                request.Items.Count);

            var created = await _purchaseDraftService.CreateAsync(request.ToCommand(), cancellationToken);

            PurchaseMemoryLogMessages.CreatedPurchaseDraft(
                _logger,
                created.PurchaseId,
                created.Issues.Count);

            return created;
        });
    }

    [McpServerTool(
        Name = "update_purchase_draft",
        Title = "Update purchase draft",
        ReadOnly = false,
        Destructive = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Use this when the user corrects a previously created purchase draft. " +
        "Send the complete corrected snapshot, including every item that should remain. " +
        "Do not call this for a new receipt.")]
    public async Task<PurchaseDraftView> UpdatePurchaseDraftAsync(
        [Description("Complete corrected purchase draft snapshot.")]
        UpdatePurchaseDraftRequest request,
        CancellationToken cancellationToken)
    {
        return await ExecuteApplicationOperationAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(request.Items);

            PurchaseMemoryLogMessages.UpdatingPurchaseDraft(
                _logger,
                request.PurchaseId,
                request.Items.Count);

            var updated = await _purchaseDraftService.UpdateAsync(request.ToCommand(), cancellationToken);

            PurchaseMemoryLogMessages.UpdatedPurchaseDraft(_logger, updated.PurchaseId);
            return updated;
        });
    }

    [McpServerTool(
        Name = "search_purchases",
        Title = "Search purchases",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Search saved purchase drafts by merchant or original receipt item name. " +
        "Use an empty query to list the ten most recent drafts. Optional category, brand and tag filters keep " +
        "purchases with at least one line whose product matches; such lines carry matchesFilter = true.")]
    public async Task<IReadOnlyList<PurchaseDraftView>> SearchPurchasesAsync(
        [Description("Merchant or item-name text to find. Use an empty string for recent purchases.")]
        string query = "",
        [Description("Category id from list_product_categories (a group includes its subcategories), or null.")]
        Guid? categoryId = null,
        [Description("Only purchases with a line whose product has no category. Cannot be combined with categoryId.")]
        bool uncategorized = false,
        [Description("Brand text, or empty for any. Case and Polish diacritics are ignored.")]
        string brand = "",
        [Description("Tag id from list_tags, or null for any.")]
        Guid? tagId = null,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteApplicationOperationAsync(async () =>
        {
            PurchaseMemoryLogMessages.SearchingPurchases(_logger, query);

            var filtered = categoryId is not null || uncategorized || !string.IsNullOrWhiteSpace(brand) || tagId is not null;
            var results = filtered
                ? (await _purchaseDraftService.GetFeedAsync(
                    new PurchaseFeedQuery
                    {
                        Query = query,
                        CategoryId = categoryId,
                        Uncategorized = uncategorized,
                        Brand = brand,
                        TagIds = tagId is { } id ? [id] : null,
                        PageSize = 10
                    },
                    cancellationToken)).Items
                : await _purchaseDraftService.SearchAsync(query, cancellationToken);

            PurchaseMemoryLogMessages.PurchaseSearchCompleted(
                _logger,
                results.Count,
                query);

            return results;
        });
    }

    /// <summary>
    /// MCP intentionally hides arbitrary exception details. Expected validation
    /// and not-found failures are safe to expose so the model can correct its
    /// arguments and retry; infrastructure failures remain private.
    /// </summary>
    private static async Task<T> ExecuteApplicationOperationAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return await operation();
        }
        catch (PurchaseDraftNotFoundException exception)
        {
            throw new McpException(exception.Message, exception);
        }
        catch (PossibleDuplicatePurchaseException exception)
        {
            throw new McpException(exception.Message, exception);
        }
        catch (ArgumentException exception)
        {
            throw new McpException(exception.Message, exception);
        }
        catch (PurchaseMemory.Application.Products.Categories.ProductCategoryNotFoundException exception)
        {
            throw new McpException(exception.Message, exception);
        }
        catch (PurchaseMemory.Application.Products.Tags.ProductTagNotFoundException exception)
        {
            throw new McpException(exception.Message, exception);
        }
    }
}
