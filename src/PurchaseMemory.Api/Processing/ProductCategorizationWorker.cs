using PurchaseMemory.Application.Products.Categories;
using PurchaseMemory.Infrastructure.Processing;

namespace PurchaseMemory.Api.Processing;

/// <summary>
/// Suggests categories for products linked by a just-saved purchase. It runs
/// off the request path, so a slow or failing model never delays or breaks a
/// save; failures are logged and the products stay unclassified.
/// </summary>
public sealed partial class ProductCategorizationWorker(
    ChannelProductCategorizationDispatcher dispatcher,
    IServiceScopeFactory scopeFactory,
    ILogger<ProductCategorizationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            IReadOnlyCollection<Guid> productIds;
            try
            {
                productIds = await dispatcher.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<IProductCategorizationService>();
                await service.SuggestForProductsAsync(productIds, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                CategorizationFailed(productIds.Count, exception);
            }
        }
    }

    [LoggerMessage(
        EventId = 3210,
        Level = LogLevel.Warning,
        Message = "Background categorization failed for {ProductCount} products; they stay uncategorized")]
    private partial void CategorizationFailed(int productCount, Exception exception);
}
