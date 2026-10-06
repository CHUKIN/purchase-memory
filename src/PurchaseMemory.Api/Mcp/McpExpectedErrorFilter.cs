using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using PurchaseMemory.Api.Logging;

namespace PurchaseMemory.Api.Mcp;

/// <summary>
/// Converts expected, model-actionable MCP failures into normal isError results.
/// Unexpected exceptions bypass this filter and retain the SDK's full error log.
/// </summary>
internal static class McpExpectedErrorFilter
{
    public static McpRequestFilter<CallToolRequestParams, CallToolResult> Create()
    {
        return next => async (request, cancellationToken) =>
        {
            try
            {
                return await next(request, cancellationToken);
            }
            catch (McpException exception)
            {
                var toolName = request.Params?.Name ?? "<unknown>";
                var loggerFactory = request.Services?.GetService<ILoggerFactory>();

                if (loggerFactory is not null)
                {
                    var logger = loggerFactory.CreateLogger(typeof(McpExpectedErrorFilter).FullName!);
                    PurchaseMemoryLogMessages.ExpectedToolCallRejected(
                        logger,
                        toolName,
                        exception.Message);
                }

                return new CallToolResult
                {
                    IsError = true,
                    Content =
                    [
                        new TextContentBlock
                        {
                            Text = $"An error occurred invoking '{toolName}': {exception.Message}"
                        }
                    ]
                };
            }
        };
    }
}
