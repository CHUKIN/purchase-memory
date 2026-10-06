using PurchaseMemory.Application.Receipts;

namespace PurchaseMemory.Api.Processing;

public sealed partial class ReceiptProcessingWorker(
    IReceiptProcessingDispatcher dispatcher,
    IServiceScopeFactory scopeFactory,
    ILogger<ReceiptProcessingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var workerCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var queueConsumer = ConsumeQueueAsync(workerCancellation.Token);
        try
        {
            // The consumer must be running before recovery starts writing into
            // the bounded channel. Otherwise >32 durable jobs deadlock startup.
            await RecoverPendingAsync(workerCancellation.Token);
            await queueConsumer;
        }
        finally
        {
            await workerCancellation.CancelAsync();
            try
            {
                await queueConsumer;
            }
            catch (OperationCanceledException) when (workerCancellation.IsCancellationRequested)
            {
                // Normal worker shutdown.
            }
        }
    }

    private async Task ConsumeQueueAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            Guid receiptId;
            try
            {
                receiptId = await dispatcher.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<IReceiptProcessingService>();
                await service.ProcessAsync(receiptId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                ProcessingFailed(receiptId, exception);
            }
        }
    }

    private async Task RecoverPendingAsync(CancellationToken stoppingToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IReceiptProcessingService>();
        var recovered = await service.RecoverPendingAsync(stoppingToken);
        if (recovered > 0)
        {
            PendingRecovered(recovered);
        }
    }

    [LoggerMessage(EventId = 3200, Level = LogLevel.Information, Message = "Recovered {ReceiptCount} pending receipt jobs")]
    private partial void PendingRecovered(int receiptCount);

    [LoggerMessage(
        EventId = 3201,
        Level = LogLevel.Error,
        Message = "Background recognition failed for receipt {ReceiptId}")]
    private partial void ProcessingFailed(Guid receiptId, Exception exception);
}
