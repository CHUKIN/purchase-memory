using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using PurchaseMemory.Application.Purchases;
using PurchaseMemory.Application.Receipts;
using PurchaseMemory.Domain.Receipts;

namespace PurchaseMemory.IntegrationTests;

public sealed class ReceiptReviewAuditTests
{
    [Fact]
    public async Task ReviewRecordsTheSignedInActorAndEmail()
    {
        await using var application = await TestApplication.CreateAsync();
        await using var scope = application.Services.CreateAsyncScope();
        var service = CreateService(scope.ServiceProvider);
        var uploaded = await UploadAndProcessAsync(service);

        await service.ReviewAsync(
            new ReviewReceiptCommand(
                uploaded.ReceiptId,
                "Lidl",
                uploaded.PurchasedAt,
                "PLN",
                uploaded.ReceiptTotal,
                uploaded.Items.Select(item => new ReceiptItemCommand(
                    item.Name, item.Quantity, item.Unit, item.UnitPrice, item.LineTotal)).ToList(),
                Actor: new ReceiptActor("user-boris", "boris@example.com")),
            TestContext.Current.CancellationToken);

        var trail = await service.GetAuditTrailAsync(uploaded.ReceiptId, TestContext.Current.CancellationToken);
        var entry = Assert.Single(trail, item => item.FieldName == "MerchantName");
        Assert.Equal("user-boris", entry.Actor);
        Assert.Equal("boris@example.com", entry.ActorEmail);
    }

    [Fact]
    public async Task ReviewWithFieldEditAndItemRemovalRecordsAuditEntries()
    {
        await using var application = await TestApplication.CreateAsync();
        await using var scope = application.Services.CreateAsyncScope();
        var service = CreateService(scope.ServiceProvider);
        var uploaded = await UploadAndProcessAsync(service);

        var reviewed = await service.ReviewAsync(
            new ReviewReceiptCommand(
                uploaded.ReceiptId,
                "Biedronka Polska", // changed from "Biedronka"
                uploaded.PurchasedAt,
                "PLN",
                6.49m,
                [new ReceiptItemCommand("MLEKO", 1, "szt", 6.49m, 6.49m)]), // second item removed
            TestContext.Current.CancellationToken);

        Assert.Equal("Biedronka Polska", reviewed.MerchantName);

        var trail = await service.GetAuditTrailAsync(uploaded.ReceiptId, TestContext.Current.CancellationToken);

        var merchantEntry = Assert.Single(trail, entry => entry.FieldName == "MerchantName");
        Assert.Equal(ReceiptReviewAuditChangeType.Modified, merchantEntry.ChangeType);
        Assert.Equal("Biedronka", merchantEntry.OldValue);
        Assert.Equal("Biedronka Polska", merchantEntry.NewValue);
        Assert.Equal(ReceiptReviewActor.Stub, merchantEntry.Actor);

        var removedEntry = Assert.Single(trail, entry => entry.ChangeType == ReceiptReviewAuditChangeType.ItemRemoved);
        Assert.Equal("Item:1", removedEntry.FieldName);
        Assert.Contains("JOGURT", removedEntry.OldValue);
        Assert.Null(removedEntry.NewValue);

        // No entries for the unchanged first item's fields.
        Assert.DoesNotContain(trail, entry => entry.FieldName.StartsWith("Item:0.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ResubmittingUnchangedReviewProducesNoAuditEntries()
    {
        await using var application = await TestApplication.CreateAsync();
        await using var scope = application.Services.CreateAsyncScope();
        var service = CreateService(scope.ServiceProvider);
        var uploaded = await UploadAndProcessAsync(service);

        var current = await service.GetAsync(uploaded.ReceiptId, TestContext.Current.CancellationToken);

        await service.ReviewAsync(
            new ReviewReceiptCommand(
                uploaded.ReceiptId,
                current.MerchantName,
                current.PurchasedAt,
                current.Currency,
                current.ReceiptTotal,
                current.Items
                    .Select(item => new ReceiptItemCommand(item.Name, item.Quantity, item.Unit, item.UnitPrice, item.LineTotal))
                    .ToList()),
            TestContext.Current.CancellationToken);

        var trail = await service.GetAuditTrailAsync(uploaded.ReceiptId, TestContext.Current.CancellationToken);
        Assert.Empty(trail);
    }

    [Fact]
    public async Task AuditEntriesPersistAcrossRepositoryReload()
    {
        await using var application = await TestApplication.CreateAsync();
        await using var scope = application.Services.CreateAsyncScope();
        var service = CreateService(scope.ServiceProvider);
        var uploaded = await UploadAndProcessAsync(service);

        await service.ReviewAsync(
            new ReviewReceiptCommand(
                uploaded.ReceiptId,
                "Renamed Shop",
                uploaded.PurchasedAt,
                "PLN",
                6.49m,
                [
                    new ReceiptItemCommand("MLEKO", 1, "szt", 6.49m, 6.49m),
                    new ReceiptItemCommand("JOGURT", 1, "szt", 3.20m, 3.20m)
                ]),
            TestContext.Current.CancellationToken);

        // A fresh scope forces a new DbContext, so this reload comes from the database.
        await using var reloadScope = application.Services.CreateAsyncScope();
        var reloadedService = CreateService(reloadScope.ServiceProvider);
        var trail = await reloadedService.GetAuditTrailAsync(uploaded.ReceiptId, TestContext.Current.CancellationToken);

        Assert.Single(trail, entry => entry.FieldName == "MerchantName" && entry.NewValue == "Renamed Shop");
    }

    private static ReceiptProcessingService CreateService(IServiceProvider services)
    {
        return new ReceiptProcessingService(
            services.GetRequiredService<IReceiptCaptureRepository>(),
            new FakeReceiptImageStore(),
            new FakeReceiptExtractor(),
            new FakeReceiptProcessingDispatcher(),
            services.GetRequiredService<IPurchaseDraftService>(),
            services.GetRequiredService<TimeProvider>());
    }

    private static async Task<ReceiptCaptureView> UploadAndProcessAsync(ReceiptProcessingService service)
    {
        await using var image = new MemoryStream("fake-image"u8.ToArray());
        var uploaded = (await service.UploadAsync(
            new UploadReceiptCommand([new ReceiptImageUpload(image, "image/jpeg")]),
            TestContext.Current.CancellationToken)).Receipt;

        await service.QueueAsync(uploaded.ReceiptId, TestContext.Current.CancellationToken);
        return await service.ProcessAsync(uploaded.ReceiptId, TestContext.Current.CancellationToken);
    }

    private sealed class FakeReceiptExtractor : IReceiptExtractor
    {
        public Task<ReceiptExtractionResult> ExtractAsync(
            IReadOnlyList<ReceiptImageData> images,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new ReceiptExtractionResult(
                "Biedronka",
                new DateTimeOffset(2026, 7, 15, 13, 30, 0, TimeSpan.FromHours(2)),
                "PLN",
                6.49m,
                [
                    new ReceiptExtractionItem("MLEKO", 1, "szt", 6.49m, 6.49m),
                    new ReceiptExtractionItem("JOGURT", 1, "szt", 3.20m, 3.20m)
                ],
                "fake",
                "fake-vision"));
        }
    }

    private sealed class FakeReceiptImageStore : IReceiptImageStore
    {
        private readonly Dictionary<string, byte[]> _images = [];

        public Task<StoredReceiptImage> StoreAsync(
            Guid receiptId,
            int position,
            Stream image,
            string declaredContentType,
            CancellationToken cancellationToken)
        {
            using var buffer = new MemoryStream();
            image.CopyTo(buffer);
            var bytes = buffer.ToArray();
            var storageKey = $"{receiptId:N}/part-{position:D3}.jpg";
            _images[storageKey] = bytes;
            return Task.FromResult(new StoredReceiptImage(
                storageKey,
                declaredContentType,
                bytes.Length,
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()));
        }

        public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken)
        {
            Stream stream = new MemoryStream(_images[storageKey], writable: false);
            return Task.FromResult(stream);
        }

        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
        {
            _images.Remove(storageKey);
            return Task.CompletedTask;
        }

        public Task DeleteReceiptAsync(Guid receiptId, CancellationToken cancellationToken)
        {
            foreach (var key in _images.Keys.Where(key => key.StartsWith($"{receiptId:N}/", StringComparison.Ordinal)).ToList())
            {
                _images.Remove(key);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FakeReceiptProcessingDispatcher : IReceiptProcessingDispatcher
    {
        public ValueTask EnqueueAsync(Guid receiptId, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Guid.Empty);
    }
}
