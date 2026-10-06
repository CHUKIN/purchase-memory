using System.Security.Cryptography;
using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using PurchaseMemory.Application.Purchases;
using PurchaseMemory.Application.Receipts;
using PurchaseMemory.Domain.Receipts;

namespace PurchaseMemory.IntegrationTests;

public sealed class ReceiptProcessingServiceTests
{
    [Fact]
    public async Task HappyFlowUploadsProcessesReviewsAndSavesPurchase()
    {
        await using var application = await TestApplication.CreateAsync();
        await using var scope = application.Services.CreateAsyncScope();
        var imageStore = new FakeReceiptImageStore();
        var extractor = new FakeReceiptExtractor(CreateExtractionResult());
        var dispatcher = new FakeReceiptProcessingDispatcher();
        var service = CreateService(scope.ServiceProvider, imageStore, extractor, dispatcher);

        var uploaded = await UploadAsync(service, TestContext.Current.CancellationToken);
        Assert.Equal(ReceiptProcessingStatus.Uploaded, uploaded.Status);

        var queued = await service.QueueAsync(uploaded.ReceiptId, TestContext.Current.CancellationToken);
        Assert.Equal(ReceiptProcessingStatus.Queued, queued.Status);
        Assert.Equal(uploaded.ReceiptId, await dispatcher.DequeueAsync(TestContext.Current.CancellationToken));

        var processed = await service.ProcessAsync(uploaded.ReceiptId, TestContext.Current.CancellationToken);
        Assert.Equal(ReceiptProcessingStatus.ReadyForReview, processed.Status);
        Assert.Equal("Biedronka", processed.MerchantName);
        Assert.Equal("MLEKO", Assert.Single(processed.Items).Name);

        var reviewed = await service.ReviewAsync(
            new ReviewReceiptCommand(
                uploaded.ReceiptId,
                "Biedronka Polska",
                processed.PurchasedAt,
                "pln",
                6.49m,
                [new ReceiptItemCommand("MLEKO 3,2%", 1, "szt", 6.49m, 6.49m)]),
            TestContext.Current.CancellationToken);

        Assert.Equal("Biedronka Polska", reviewed.MerchantName);
        Assert.Equal("PLN", reviewed.Currency);
        Assert.Empty(reviewed.Warnings);

        var purchase = await service.SaveAsync(
            new SaveReceiptCommand(uploaded.ReceiptId),
            TestContext.Current.CancellationToken);
        var savedReceipt = await service.GetAsync(uploaded.ReceiptId, TestContext.Current.CancellationToken);

        Assert.Equal(uploaded.ReceiptId, purchase.SourceReceiptId);
        Assert.Equal(ReceiptProcessingStatus.Saved, savedReceipt.Status);
        Assert.Equal(purchase.PurchaseId, savedReceipt.SavedPurchaseId);
        Assert.Equal("MLEKO 3,2%", Assert.Single(purchase.Items).Name);
    }

    [Fact]
    public async Task DiscountLinesAreKeptLinkedToTheirProductAndNetOffTheTotal()
    {
        await using var application = await TestApplication.CreateAsync();
        await using var scope = application.Services.CreateAsyncScope();
        var dispatcher = new FakeReceiptProcessingDispatcher();
        var extraction = ReceiptExtractionPostprocessor.Apply(new ReceiptExtractionResult(
            "Lidl",
            new DateTimeOffset(2026, 8, 20, 18, 0, 0, TimeSpan.FromHours(2)),
            "PLN",
            110.48m,
            [
                new ReceiptExtractionItem("Odkurzacz", 1, null, 99.99m, 99.99m),
                new ReceiptExtractionItem("OPUST", null, null, null, -30.00m),
                new ReceiptExtractionItem("Mleko", 2, null, 4.49m, 8.98m),
                new ReceiptExtractionItem("Chleb", 1, null, 3.29m, 3.29m),
                new ReceiptExtractionItem("RABAT Chleb", null, null, null, -0.78m),
                new ReceiptExtractionItem("Kawa", 1, null, 29.00m, 29.00m)
            ],
            "fake",
            "fake-vision"));
        var service = CreateService(
            scope.ServiceProvider,
            new FakeReceiptImageStore(),
            new FakeReceiptExtractor(extraction),
            dispatcher);

        var uploaded = await UploadAsync(service, TestContext.Current.CancellationToken);
        await service.QueueAsync(uploaded.ReceiptId, TestContext.Current.CancellationToken);
        await dispatcher.DequeueAsync(TestContext.Current.CancellationToken);
        var processed = await service.ProcessAsync(uploaded.ReceiptId, TestContext.Current.CancellationToken);

        Assert.Equal(6, processed.Items.Count);
        Assert.Equal(ReceiptLineKind.Discount, processed.Items[1].Kind);
        Assert.Equal(0, processed.Items[1].DiscountedItemPosition);
        Assert.Equal(3, processed.Items[4].DiscountedItemPosition);
        Assert.All(
            processed.Items.Where(item => item.Kind == ReceiptLineKind.Product),
            item => Assert.Null(item.DiscountedItemPosition));
        // 99.99 - 30.00 + 8.98 + 3.29 - 0.78 + 29.00 = 110.48: no mismatch, and a
        // discount without a unit price is not a "missing price" issue.
        Assert.Empty(processed.Warnings);
        Assert.Empty(processed.Issues);

        var purchase = await service.SaveAsync(
            new SaveReceiptCommand(uploaded.ReceiptId),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            [ReceiptLineKind.Product, ReceiptLineKind.Discount, ReceiptLineKind.Product,
             ReceiptLineKind.Product, ReceiptLineKind.Discount, ReceiptLineKind.Product],
            purchase.Items.Select(item => item.Kind));
        Assert.Equal(0, purchase.Items[1].DiscountedItemPosition);
        Assert.Equal(3, purchase.Items[4].DiscountedItemPosition);
        Assert.Empty(purchase.Issues);
    }

    [Fact]
    public async Task ReviewRecomputesDiscountLinksAndWarnsAboutADiscountWithoutProduct()
    {
        await using var application = await TestApplication.CreateAsync();
        await using var scope = application.Services.CreateAsyncScope();
        var dispatcher = new FakeReceiptProcessingDispatcher();
        var service = CreateService(
            scope.ServiceProvider,
            new FakeReceiptImageStore(),
            new FakeReceiptExtractor(CreateExtractionResult()),
            dispatcher);
        var uploaded = await UploadAsync(service, TestContext.Current.CancellationToken);
        await service.QueueAsync(uploaded.ReceiptId, TestContext.Current.CancellationToken);
        await dispatcher.DequeueAsync(TestContext.Current.CancellationToken);
        var processed = await service.ProcessAsync(uploaded.ReceiptId, TestContext.Current.CancellationToken);

        // The client sends no kind for the first row and an explicit one for the
        // second; a negative amount alone is enough to make a discount.
        var reviewed = await service.ReviewAsync(
            new ReviewReceiptCommand(
                uploaded.ReceiptId,
                "Lidl",
                processed.PurchasedAt,
                "PLN",
                5.49m,
                [
                    new ReceiptItemCommand("OPUST", null, null, null, -1.00m),
                    new ReceiptItemCommand("MLEKO", 1, "szt", 6.49m, 6.49m, Kind: ReceiptLineKind.Product)
                ]),
            TestContext.Current.CancellationToken);

        Assert.Equal(ReceiptLineKind.Discount, reviewed.Items[0].Kind);
        Assert.Null(reviewed.Items[0].DiscountedItemPosition);
        var warning = Assert.Single(reviewed.Warnings);
        Assert.Contains("OPUST", warning, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AReceiptLevelDiscountStaysUnlinkedWithoutAWarning()
    {
        var (service, receiptId, _) = await ProcessedAsync(new ReceiptExtractionResult(
            "Żabka", null, "PLN", 56.38m,
            [
                new ReceiptExtractionItem("Pizza", 1, null, 79.97m, 79.97m),
                new ReceiptExtractionItem("OPUST (podsuma)", null, null, null, -21.59m, Kind: ReceiptLineKind.Discount),
                new ReceiptExtractionItem("Pudełko", 1, null, 2m, 2m),
                new ReceiptExtractionItem("OPUST", null, null, null, -2m, Kind: ReceiptLineKind.Discount),
                new ReceiptExtractionItem("OPUST", null, null, null, -2m, Kind: ReceiptLineKind.Discount)
            ],
            "fake", "fake-vision"));

        var view = await service.GetAsync(receiptId, TestContext.Current.CancellationToken);

        Assert.Null(view.Items[1].DiscountedItemPosition);
        Assert.Equal(2, view.Items[3].DiscountedItemPosition);
        // The second -2.00 would take Pudełko below zero only together with the
        // first, but each discount is judged against the product on its own.
        Assert.Equal(2, view.Items[4].DiscountedItemPosition);
        Assert.DoesNotContain(view.Warnings, warning => warning.Contains("не относится", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(ReceiptLineKind.Product, -1.0)]
    [InlineData(ReceiptLineKind.Discount, 1.0)]
    public async Task ReviewRejectsAKindThatContradictsTheAmountsSign(ReceiptLineKind kind, double amount)
    {
        var (service, receiptId, _) = await ProcessedAsync(CreateExtractionResult());

        var exception = await Assert.ThrowsAnyAsync<ArgumentException>(() => service.ReviewAsync(
            new ReviewReceiptCommand(
                receiptId, "Lidl", null, "PLN", 6.49m,
                [
                    new ReceiptItemCommand("MLEKO", 1, "szt", 6.49m, 6.49m),
                    new ReceiptItemCommand("OPUST", null, null, null, (decimal)amount, Kind: kind)
                ]),
            TestContext.Current.CancellationToken));

        Assert.Contains(kind == ReceiptLineKind.Product ? "product line cannot" : "discount line cannot", exception.Message, StringComparison.Ordinal);
    }

    private static async Task<(ReceiptProcessingService Service, Guid ReceiptId, TestApplication Application)> ProcessedAsync(
        ReceiptExtractionResult extraction)
    {
        var application = await TestApplication.CreateAsync();
        var scope = application.Services.CreateAsyncScope();
        var dispatcher = new FakeReceiptProcessingDispatcher();
        var service = CreateService(scope.ServiceProvider, new FakeReceiptImageStore(), new FakeReceiptExtractor(extraction), dispatcher);
        var uploaded = await UploadAsync(service, TestContext.Current.CancellationToken);
        await service.QueueAsync(uploaded.ReceiptId, TestContext.Current.CancellationToken);
        await dispatcher.DequeueAsync(TestContext.Current.CancellationToken);
        await service.ProcessAsync(uploaded.ReceiptId, TestContext.Current.CancellationToken);
        return (service, uploaded.ReceiptId, application);
    }

    [Fact]
    public async Task LineTotalMismatchIsFlaggedAsWarning()
    {
        await using var application = await TestApplication.CreateAsync();
        await using var scope = application.Services.CreateAsyncScope();
        var imageStore = new FakeReceiptImageStore();
        var mismatched = new ReceiptExtractionResult(
            "Biedronka",
            new DateTimeOffset(2026, 7, 15, 13, 30, 0, TimeSpan.FromHours(2)),
            "PLN",
            10.00m,
            [new ReceiptExtractionItem("MLEKO", 1, "szt", 6.49m, 6.49m)],
            "fake",
            "fake-vision");
        var extractor = new FakeReceiptExtractor(mismatched);
        var dispatcher = new FakeReceiptProcessingDispatcher();
        var service = CreateService(scope.ServiceProvider, imageStore, extractor, dispatcher);

        var uploaded = await UploadAsync(service, TestContext.Current.CancellationToken);
        await service.QueueAsync(uploaded.ReceiptId, TestContext.Current.CancellationToken);
        await dispatcher.DequeueAsync(TestContext.Current.CancellationToken);
        var processed = await service.ProcessAsync(uploaded.ReceiptId, TestContext.Current.CancellationToken);

        Assert.Contains(
            processed.Warnings,
            warning => warning.Contains("не совпадает", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MatchingLineTotalsProduceNoMismatchWarning()
    {
        await using var application = await TestApplication.CreateAsync();
        await using var scope = application.Services.CreateAsyncScope();
        var imageStore = new FakeReceiptImageStore();
        var extractor = new FakeReceiptExtractor(CreateExtractionResult());
        var dispatcher = new FakeReceiptProcessingDispatcher();
        var service = CreateService(scope.ServiceProvider, imageStore, extractor, dispatcher);

        var uploaded = await UploadAsync(service, TestContext.Current.CancellationToken);
        await service.QueueAsync(uploaded.ReceiptId, TestContext.Current.CancellationToken);
        await dispatcher.DequeueAsync(TestContext.Current.CancellationToken);
        var processed = await service.ProcessAsync(uploaded.ReceiptId, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(
            processed.Warnings,
            warning => warning.Contains("не совпадает", StringComparison.Ordinal));
    }

    [Fact]
    public async Task KnownExtractionFailureIsPersistedAndCanBeRetried()
    {
        await using var application = await TestApplication.CreateAsync();
        await using var scope = application.Services.CreateAsyncScope();
        var imageStore = new FakeReceiptImageStore();
        var extractor = new FakeReceiptExtractor(CreateExtractionResult())
        {
            Failure = new ReceiptExtractionException("Ollama is temporarily unavailable.")
        };
        var dispatcher = new FakeReceiptProcessingDispatcher();
        var service = CreateService(scope.ServiceProvider, imageStore, extractor, dispatcher);
        var uploaded = await UploadAsync(service, TestContext.Current.CancellationToken);

        await service.QueueAsync(uploaded.ReceiptId, TestContext.Current.CancellationToken);
        await dispatcher.DequeueAsync(TestContext.Current.CancellationToken);
        var failed = await service.ProcessAsync(uploaded.ReceiptId, TestContext.Current.CancellationToken);

        Assert.Equal(ReceiptProcessingStatus.Failed, failed.Status);
        Assert.Equal("Ollama is temporarily unavailable.", failed.ErrorMessage);

        extractor.Failure = null;
        await service.QueueAsync(uploaded.ReceiptId, TestContext.Current.CancellationToken);
        await dispatcher.DequeueAsync(TestContext.Current.CancellationToken);
        var retried = await service.ProcessAsync(uploaded.ReceiptId, TestContext.Current.CancellationToken);

        Assert.Equal(ReceiptProcessingStatus.ReadyForReview, retried.Status);
        Assert.Null(retried.ErrorMessage);
    }

    [Fact]
    public async Task SavingSameReceiptTwiceReturnsOnePurchase()
    {
        await using var application = await TestApplication.CreateAsync();
        await using var scope = application.Services.CreateAsyncScope();
        var dispatcher = new FakeReceiptProcessingDispatcher();
        var service = CreateService(
            scope.ServiceProvider,
            new FakeReceiptImageStore(),
            new FakeReceiptExtractor(CreateExtractionResult()),
            dispatcher);
        var uploaded = await UploadAsync(service, TestContext.Current.CancellationToken);
        await service.QueueAsync(uploaded.ReceiptId, TestContext.Current.CancellationToken);
        await dispatcher.DequeueAsync(TestContext.Current.CancellationToken);
        await service.ProcessAsync(uploaded.ReceiptId, TestContext.Current.CancellationToken);

        var first = await service.SaveAsync(
            new SaveReceiptCommand(uploaded.ReceiptId),
            TestContext.Current.CancellationToken);
        var second = await service.SaveAsync(
            new SaveReceiptCommand(uploaded.ReceiptId),
            TestContext.Current.CancellationToken);
        var purchaseService = scope.ServiceProvider.GetRequiredService<IPurchaseDraftService>();
        var matches = await purchaseService.SearchAsync(
            "MLEKO",
            TestContext.Current.CancellationToken);

        Assert.Equal(first.PurchaseId, second.PurchaseId);
        Assert.Equal(uploaded.ReceiptId, second.SourceReceiptId);
        Assert.Single(matches);
    }

    [Fact]
    public async Task MultipartOrderDefinesExactDuplicateAndRecentListing()
    {
        await using var application = await TestApplication.CreateAsync();
        await using var scope = application.Services.CreateAsyncScope();
        var imageStore = new FakeReceiptImageStore();
        var service = CreateService(
            scope.ServiceProvider,
            imageStore,
            new FakeReceiptExtractor(CreateExtractionResult()),
            new FakeReceiptProcessingDispatcher());

        var first = await UploadPartsAsync(service, ["top", "bottom"]);
        var duplicate = await UploadPartsAsync(service, ["top", "bottom"]);
        var reversed = await UploadPartsAsync(service, ["bottom", "top"]);
        var recent = await service.ListRecentAsync(10, TestContext.Current.CancellationToken);

        Assert.False(first.IsExactDuplicate);
        Assert.True(duplicate.IsExactDuplicate);
        Assert.Equal(first.Receipt.ReceiptId, duplicate.Receipt.ReceiptId);
        Assert.False(reversed.IsExactDuplicate);
        Assert.NotEqual(first.Receipt.ReceiptId, reversed.Receipt.ReceiptId);
        Assert.Equal([0, 1], first.Receipt.ImageParts.Select(part => part.Position));
        Assert.Equal(2, recent.Count);
    }

    [Fact]
    public async Task RecoverPendingRequeuesInterruptedProcessingIdempotently()
    {
        await using var application = await TestApplication.CreateAsync();
        await using var scope = application.Services.CreateAsyncScope();
        var initialDispatcher = new FakeReceiptProcessingDispatcher();
        var service = CreateService(scope.ServiceProvider, new FakeReceiptImageStore(), new FakeReceiptExtractor(CreateExtractionResult()), initialDispatcher);
        var uploaded = await UploadAsync(service, TestContext.Current.CancellationToken);
        await service.QueueAsync(uploaded.ReceiptId, TestContext.Current.CancellationToken);
        await initialDispatcher.DequeueAsync(TestContext.Current.CancellationToken);
        var repository = scope.ServiceProvider.GetRequiredService<IReceiptCaptureRepository>();
        var capture = await repository.FindByIdAsync(uploaded.ReceiptId, TestContext.Current.CancellationToken);
        Assert.NotNull(capture);
        capture.StartProcessing(DateTimeOffset.UtcNow);
        await repository.SaveChangesAsync(TestContext.Current.CancellationToken);

        var recoveredDispatcher = new FakeReceiptProcessingDispatcher();
        var recoveryService = CreateService(scope.ServiceProvider, new FakeReceiptImageStore(), new FakeReceiptExtractor(CreateExtractionResult()), recoveredDispatcher);
        var count = await recoveryService.RecoverPendingAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, count);
        Assert.Equal(uploaded.ReceiptId, await recoveredDispatcher.DequeueAsync(TestContext.Current.CancellationToken));
        Assert.Equal(ReceiptProcessingStatus.Queued, (await recoveryService.GetAsync(uploaded.ReceiptId, TestContext.Current.CancellationToken)).Status);
    }

    [Fact]
    public async Task MultipartStorageFailureCleansAlreadyStoredParts()
    {
        await using var application = await TestApplication.CreateAsync();
        await using var scope = application.Services.CreateAsyncScope();
        var imageStore = new FakeReceiptImageStore { FailAtPosition = 1 };
        var service = CreateService(scope.ServiceProvider, imageStore, new FakeReceiptExtractor(CreateExtractionResult()), new FakeReceiptProcessingDispatcher());

        await Assert.ThrowsAsync<InvalidDataException>(() => UploadPartsAsync(service, ["top", "bottom"]));

        Assert.Equal(0, imageStore.StoredCount);
        Assert.Empty(await service.ListRecentAsync(10, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ConcurrentExactUploadsReturnOneCaptureWithoutDeletingWinnerFiles()
    {
        await using var application = await TestApplication.CreateAsync();
        await using var firstScope = application.Services.CreateAsyncScope();
        await using var secondScope = application.Services.CreateAsyncScope();
        var imageStore = new FakeReceiptImageStore();
        var precheck = new ExactDuplicatePrecheck();
        var firstRepository = new CoordinatedReceiptCaptureRepository(
            firstScope.ServiceProvider.GetRequiredService<IReceiptCaptureRepository>(),
            precheck);
        var secondRepository = new CoordinatedReceiptCaptureRepository(
            secondScope.ServiceProvider.GetRequiredService<IReceiptCaptureRepository>(),
            precheck);
        var firstService = CreateService(firstScope.ServiceProvider, imageStore, new FakeReceiptExtractor(CreateExtractionResult()), new FakeReceiptProcessingDispatcher(), firstRepository);
        var secondService = CreateService(secondScope.ServiceProvider, imageStore, new FakeReceiptExtractor(CreateExtractionResult()), new FakeReceiptProcessingDispatcher(), secondRepository);

        var results = await Task.WhenAll(
            UploadPartsAsync(firstService, ["same-top", "same-bottom"]),
            UploadPartsAsync(secondService, ["same-top", "same-bottom"]));

        Assert.Single(results, result => !result.IsExactDuplicate);
        Assert.Single(results, result => result.IsExactDuplicate);
        Assert.Equal(results[0].Receipt.ReceiptId, results[1].Receipt.ReceiptId);
        Assert.Equal(2, imageStore.StoredCount);
    }

    private static ReceiptProcessingService CreateService(
        IServiceProvider services,
        IReceiptImageStore imageStore,
        IReceiptExtractor extractor,
        IReceiptProcessingDispatcher dispatcher,
        IReceiptCaptureRepository? repository = null)
    {
        return new ReceiptProcessingService(
            repository ?? services.GetRequiredService<IReceiptCaptureRepository>(),
            imageStore,
            extractor,
            dispatcher,
            services.GetRequiredService<IPurchaseDraftService>(),
            services.GetRequiredService<TimeProvider>());
    }

    private sealed class ExactDuplicatePrecheck
    {
        private int _started;
        private int _completed;

        public TaskCompletionSource BothStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource BothCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void MarkStarted()
        {
            if (Interlocked.Increment(ref _started) == 2)
            {
                BothStarted.TrySetResult();
            }
        }

        public void MarkCompleted()
        {
            if (Interlocked.Increment(ref _completed) == 2)
            {
                BothCompleted.TrySetResult();
            }
        }
    }

    private sealed class CoordinatedReceiptCaptureRepository(
        IReceiptCaptureRepository inner,
        ExactDuplicatePrecheck precheck) : IReceiptCaptureRepository
    {
        public Task<ReceiptCapture?> AddOrFindExactDuplicateAsync(
            ReceiptCapture receipt,
            CancellationToken cancellationToken) =>
            inner.AddOrFindExactDuplicateAsync(receipt, cancellationToken);

        public Task<ReceiptCapture?> FindByIdAsync(Guid receiptId, CancellationToken cancellationToken) =>
            inner.FindByIdAsync(receiptId, cancellationToken);

        public async Task<ReceiptCapture?> FindByImageSetSha256Async(
            string imageSetSha256,
            CancellationToken cancellationToken)
        {
            precheck.MarkStarted();
            await precheck.BothStarted.Task.WaitAsync(cancellationToken);
            var result = await inner.FindByImageSetSha256Async(imageSetSha256, cancellationToken);
            precheck.MarkCompleted();
            await precheck.BothCompleted.Task.WaitAsync(cancellationToken);
            return result;
        }

        public Task<IReadOnlyList<ReceiptCapture>> ListRecentAsync(int limit, CancellationToken cancellationToken) =>
            inner.ListRecentAsync(limit, cancellationToken);

        public Task<IReadOnlyList<ReceiptCapture>> ListPendingAsync(CancellationToken cancellationToken) =>
            inner.ListPendingAsync(cancellationToken);

        public Task SaveChangesAsync(CancellationToken cancellationToken) =>
            inner.SaveChangesAsync(cancellationToken);
    }

    private static async Task<ReceiptCaptureView> UploadAsync(
        ReceiptProcessingService service,
        CancellationToken cancellationToken)
    {
        await using var image = new MemoryStream("fake-image"u8.ToArray());
        return (await service.UploadAsync(
            new UploadReceiptCommand([new ReceiptImageUpload(image, "image/jpeg")]),
            cancellationToken)).Receipt;
    }

    private static async Task<UploadReceiptResult> UploadPartsAsync(
        ReceiptProcessingService service,
        IReadOnlyList<string> contents)
    {
        var streams = contents.Select(content => new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content))).ToList();
        try
        {
            return await service.UploadAsync(
                new UploadReceiptCommand(streams.Select(stream => new ReceiptImageUpload(stream, "image/jpeg")).ToList()),
                TestContext.Current.CancellationToken);
        }
        finally
        {
            foreach (var stream in streams)
            {
                await stream.DisposeAsync();
            }
        }
    }

    private static ReceiptExtractionResult CreateExtractionResult()
    {
        return new ReceiptExtractionResult(
            "Biedronka",
            new DateTimeOffset(2026, 7, 15, 13, 30, 0, TimeSpan.FromHours(2)),
            "PLN",
            6.49m,
            [new ReceiptExtractionItem("MLEKO", 1, "szt", 6.49m, 6.49m)],
            "fake",
            "fake-vision");
    }

    private sealed class FakeReceiptExtractor(ReceiptExtractionResult result) : IReceiptExtractor
    {
        public ReceiptExtractionException? Failure { get; set; }

        public Task<ReceiptExtractionResult> ExtractAsync(
            IReadOnlyList<ReceiptImageData> images,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.NotEmpty(images);
            Assert.All(images, image => Assert.Equal("image/jpeg", image.ContentType));

            if (Failure is not null)
            {
                throw Failure;
            }

            return Task.FromResult(result);
        }
    }

    private sealed class FakeReceiptImageStore : IReceiptImageStore
    {
        private readonly ConcurrentDictionary<string, byte[]> _images = [];

        public int? FailAtPosition { get; init; }

        public int StoredCount => _images.Count;

        public async Task<StoredReceiptImage> StoreAsync(
            Guid receiptId,
            int position,
            Stream image,
            string declaredContentType,
            CancellationToken cancellationToken)
        {
            if (position == FailAtPosition)
            {
                throw new InvalidDataException("Simulated partial multipart failure.");
            }

            using var buffer = new MemoryStream();
            await image.CopyToAsync(buffer, cancellationToken);
            var bytes = buffer.ToArray();
            var storageKey = $"{receiptId:N}/part-{position:D3}.jpg";
            Assert.True(_images.TryAdd(storageKey, bytes));

            return new StoredReceiptImage(
                storageKey,
                declaredContentType,
                bytes.Length,
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        }

        public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Stream stream = new MemoryStream(_images[storageKey], writable: false);
            return Task.FromResult(stream);
        }

        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _images.TryRemove(storageKey, out _);
            return Task.CompletedTask;
        }

        public Task DeleteReceiptAsync(Guid receiptId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var key in _images.Keys.Where(key => key.StartsWith($"{receiptId:N}/", StringComparison.Ordinal)).ToList())
            {
                _images.TryRemove(key, out _);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FakeReceiptProcessingDispatcher : IReceiptProcessingDispatcher
    {
        private readonly Queue<Guid> _receiptIds = new();

        public ValueTask EnqueueAsync(Guid receiptId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _receiptIds.Enqueue(receiptId);
            return ValueTask.CompletedTask;
        }

        public ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(_receiptIds.Dequeue());
        }
    }
}
