using PurchaseMemory.Application.Receipts.TextLocation;
using PurchaseMemory.Infrastructure.Processing;

namespace PurchaseMemory.Api.Processing;

/// <summary>
/// Finds the printed lines of recognised receipts with Azure, one receipt at
/// a time, new receipts first and then the backfill of older ones, paced for
/// the free tier. It starts after a delay and never touches the request path:
/// review and saving work the same whether it is running, paused or disabled.
/// </summary>
public sealed partial class ReceiptTextLocationWorker(
    IServiceScopeFactory scopeFactory,
    IReceiptTextLocator locator,
    ChannelReceiptTextLocationSignal signal,
    ReceiptTextLocationOptions options,
    TimeProvider timeProvider,
    ILogger<ReceiptTextLocationWorker> logger) : BackgroundService
{
    private static readonly TimeSpan FailurePause = TimeSpan.FromMinutes(1);

    private bool _budgetReported;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled || !locator.IsConfigured)
        {
            Disabled(options.Enabled ? "AZURE_DOCINTEL_ENDPOINT / AZURE_DOCINTEL_KEY are not set" : "TextLocation:Enabled is false");
            return;
        }

        Started(options.MonthlyCallLimit, options.BackfillMonthlyCallLimit, options.SecondsBetweenReceipts);
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(options.StartupDelaySeconds), timeProvider, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                var wait = await RunOnceAsync(stoppingToken);
                if (wait is { } pause)
                {
                    await Task.Delay(pause, timeProvider, stoppingToken);
                }
                else
                {
                    await signal.WaitAsync(TimeSpan.FromMinutes(options.IdlePollMinutes), stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    /// <summary>The pause before the next receipt, or null to wait for the doorbell / idle poll.</summary>
    private async Task<TimeSpan?> RunOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IReceiptTextLocationService>();
            var run = await service.LocateNextAsync(stoppingToken);
            if (run.Outcome != ReceiptTextLocationOutcome.BudgetExhausted)
            {
                _budgetReported = false;
            }

            switch (run.Outcome)
            {
                case ReceiptTextLocationOutcome.Skipped:
                    return TimeSpan.Zero;
                case ReceiptTextLocationOutcome.Located:
                case ReceiptTextLocationOutcome.Failed:
                    return TimeSpan.FromSeconds(options.SecondsBetweenReceipts);
                case ReceiptTextLocationOutcome.Paused:
                    var pause = run.Wait ?? FailurePause;
                    Paused((int)pause.TotalSeconds);
                    return pause;
                case ReceiptTextLocationOutcome.BudgetExhausted:
                    if (!_budgetReported)
                    {
                        BudgetExhausted();
                        _budgetReported = true;
                    }

                    return null;
                default:
                    return null;
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // A database hiccup or a bug must not end the worker; the receipt
            // stays as it was and is picked up again after the pause.
            RunFailed(exception);
            return FailurePause;
        }
    }

    [LoggerMessage(EventId = 3320, Level = LogLevel.Information,
        Message = "Text location (Azure) is off: {Reason}; lines keep the recognition box")]
    private partial void Disabled(string reason);

    [LoggerMessage(EventId = 3321, Level = LogLevel.Information,
        Message = "Text location (Azure) is on: up to {MonthlyLimit} calls a month ({BackfillLimit} for older receipts), {Seconds} s between receipts")]
    private partial void Started(int monthlyLimit, int backfillLimit, int seconds);

    [LoggerMessage(EventId = 3322, Level = LogLevel.Information,
        Message = "Text location paused for {Seconds} s")]
    private partial void Paused(int seconds);

    [LoggerMessage(EventId = 3323, Level = LogLevel.Information,
        Message = "Text location: this month's call budget is used up; new receipts keep the recognition box")]
    private partial void BudgetExhausted();

    [LoggerMessage(EventId = 3324, Level = LogLevel.Error,
        Message = "Text location run failed; retrying after a pause")]
    private partial void RunFailed(Exception exception);
}
