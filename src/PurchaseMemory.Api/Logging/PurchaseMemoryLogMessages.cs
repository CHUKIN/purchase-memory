namespace PurchaseMemory.Api.Logging;

/// <summary>
/// Compile-time generated logging methods avoid allocations when a log level is
/// disabled and keep event identifiers stable for future observability tooling.
/// </summary>
internal static partial class PurchaseMemoryLogMessages
{
    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "Creating purchase draft for merchant {MerchantName} with {ItemCount} items")]
    public static partial void CreatingPurchaseDraft(
        ILogger logger,
        string merchantName,
        int itemCount);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Information,
        Message = "Created purchase draft {PurchaseId} with {WarningCount} validation warnings")]
    public static partial void CreatedPurchaseDraft(
        ILogger logger,
        Guid purchaseId,
        int warningCount);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Information,
        Message = "Updating purchase draft {PurchaseId} with {ItemCount} items")]
    public static partial void UpdatingPurchaseDraft(
        ILogger logger,
        Guid purchaseId,
        int itemCount);

    [LoggerMessage(
        EventId = 1004,
        Level = LogLevel.Information,
        Message = "Updated purchase draft {PurchaseId}")]
    public static partial void UpdatedPurchaseDraft(ILogger logger, Guid purchaseId);

    [LoggerMessage(
        EventId = 1005,
        Level = LogLevel.Information,
        Message = "Searching purchase drafts with query {SearchQuery}")]
    public static partial void SearchingPurchases(ILogger logger, string searchQuery);

    [LoggerMessage(
        EventId = 1006,
        Level = LogLevel.Information,
        Message = "Purchase search returned {ResultCount} results for query {SearchQuery}")]
    public static partial void PurchaseSearchCompleted(
        ILogger logger,
        int resultCount,
        string searchQuery);

    [LoggerMessage(
        EventId = 1007,
        Level = LogLevel.Information,
        Message = "Purchase Memory started. Database: {Database}. Receipt photos: {ImageStorage}. Logs: {LogFilePattern}. MCP: /mcp")]
    public static partial void ApplicationStarted(
        ILogger logger,
        string database,
        string imageStorage,
        string logFilePattern);

    [LoggerMessage(
        EventId = 1008,
        Level = LogLevel.Warning,
        Message = "Expected MCP tool call {ToolName} was rejected: {ErrorMessage}")]
    public static partial void ExpectedToolCallRejected(
        ILogger logger,
        string toolName,
        string errorMessage);
}
