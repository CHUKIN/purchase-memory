using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PurchaseMemory.Api.Processing;
using PurchaseMemory.Application.Purchases;
using PurchaseMemory.Application.Receipts;
using PurchaseMemory.Infrastructure.Processing;

namespace PurchaseMemory.IntegrationTests;

public sealed class ReceiptProcessingWorkerTests
{
    [Fact]
    public async Task StartupRecoveryConsumesMoreJobsThanBoundedChannelCapacity()
    {
        const int receiptCount = 40;
        var dispatcher = new ChannelReceiptProcessingDispatcher();
        var service = new RecoveringService(dispatcher, receiptCount);
        var services = new ServiceCollection()
            .AddSingleton<IReceiptProcessingService>(service)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using var worker = new ReceiptProcessingWorker(
            dispatcher,
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ReceiptProcessingWorker>.Instance);

        await worker.StartAsync(TestContext.Current.CancellationToken);
        await service.AllProcessed.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        await worker.StopAsync(TestContext.Current.CancellationToken);
        await services.DisposeAsync();

        Assert.Equal(receiptCount, service.ProcessedCount);
    }

    private sealed class RecoveringService(
        IReceiptProcessingDispatcher dispatcher,
        int receiptCount) : IReceiptProcessingService
    {
        private int _processedCount;

        public int ProcessedCount => Volatile.Read(ref _processedCount);

        public TaskCompletionSource AllProcessed { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<int> RecoverPendingAsync(CancellationToken cancellationToken)
        {
            for (var index = 0; index < receiptCount; index++)
            {
                await dispatcher.EnqueueAsync(Guid.NewGuid(), cancellationToken);
            }

            return receiptCount;
        }

        public Task<ReceiptCaptureView> ProcessAsync(Guid receiptId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Increment(ref _processedCount) == receiptCount)
            {
                AllProcessed.TrySetResult();
            }

            return Task.FromException<ReceiptCaptureView>(new ReceiptExtractionException("Expected test failure."));
        }

        public Task<UploadReceiptResult> UploadAsync(UploadReceiptCommand command, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ReceiptCaptureView>> ListRecentAsync(int limit, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ReceiptCaptureView> QueueAsync(Guid receiptId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ReceiptCaptureView> GetAsync(Guid receiptId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ReceiptReviewAuditEntryView>> GetAuditTrailAsync(Guid receiptId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ReceiptCaptureView> ReviewAsync(ReviewReceiptCommand command, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PurchaseDraftView> SaveAsync(SaveReceiptCommand command, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
