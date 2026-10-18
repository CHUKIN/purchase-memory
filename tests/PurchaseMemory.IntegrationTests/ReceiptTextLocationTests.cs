using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PurchaseMemory.Api.Endpoints;
using PurchaseMemory.Application.Products;
using PurchaseMemory.Application.Products.Categories;
using PurchaseMemory.Application.Purchases;
using PurchaseMemory.Application.Receipts;
using PurchaseMemory.Application.Receipts.TextLocation;
using PurchaseMemory.Domain.Receipts;

namespace PurchaseMemory.IntegrationTests;

/// <summary>
/// The background text-location step end to end on the real database (SQLite,
/// or Postgres when opted in): candidates, budget, retries, stored layout,
/// re-matching after edits and what the API returns. The locator is a fake
/// that answers with a real cached Azure reading of household receipt 04.
/// </summary>
public sealed class ReceiptTextLocationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task LocatesAWaitingReceiptOnceAndStoresTheOutlines()
    {
        await using var application = await TestApplication.CreateAsync();
        var locator = new FakeLocator();
        var receiptId = await AddReceiptAsync(application, Receipt04Items());

        var first = await RunAsync(application, locator);
        var second = await RunAsync(application, locator);

        Assert.Equal(ReceiptTextLocationOutcome.Located, first.Outcome);
        Assert.Equal(receiptId, first.ReceiptId);
        Assert.Equal(ReceiptTextLocationOutcome.Idle, second.Outcome);
        Assert.Equal(1, locator.Calls);

        await using var scope = application.Services.CreateAsyncScope();
        var receipt = (await scope.ServiceProvider.GetRequiredService<IReceiptCaptureRepository>().FindByIdAsync(receiptId, Cancel))!;
        Assert.Equal(ReceiptTextLocationStatus.Located, receipt.TextLocationStatus);
        Assert.Equal(1, receipt.TextLocationAttempts);
        Assert.Equal(1, receipt.TextLocationCalls);
        Assert.All(receipt.Items, item =>
        {
            Assert.Equal(ReceiptBoxSource.Azure, item.BoxSource);
            Assert.NotEmpty(item.TextPolygons);
        });
        var layout = await scope.ServiceProvider.GetRequiredService<IReceiptTextLayoutRepository>().FindAsync(receiptId, Cancel);
        Assert.NotNull(layout);
        Assert.True(layout.Lines.Count > 30);
        Assert.Equal(ReceiptTextMatcherTests.LoadLayout("04").Lines.Count, layout.Lines.Count);
    }

    [Fact]
    public async Task TheApiShowsOutlinesNextToTheRecognitionBox()
    {
        await using var application = await TestApplication.CreateAsync();
        var receiptId = await AddReceiptAsync(application, Receipt04Items());
        await RunAsync(application, new FakeLocator());

        await using var scope = application.Services.CreateAsyncScope();
        var view = await ProcessingService(scope).GetAsync(receiptId, Cancel);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ReceiptResponse.FromView(view), JsonOptions));

        var item = json.RootElement.GetProperty("snapshot").GetProperty("items")[0];
        Assert.Equal("azure", item.GetProperty("boxSource").GetString());
        Assert.Equal(0.27, item.GetProperty("boundingBox").GetProperty("x").GetDouble(), 6);
        var polygon = item.GetProperty("textPolygons")[0];
        Assert.True(polygon.GetArrayLength() >= 8);
        Assert.All(polygon.EnumerateArray(), value => Assert.InRange(value.GetDouble(), 0, 1));
    }

    [Fact]
    public async Task ReviewMatchesTheEditedLinesAgain()
    {
        await using var application = await TestApplication.CreateAsync();
        var items = Receipt04Items();
        var receiptId = await AddReceiptAsync(application, items);
        await RunAsync(application, new FakeLocator());

        // The first line renamed to something not printed (and a price not on
        // the receipt), the second deleted: the rest still finds its words.
        var edited = items
            .Select((item, index) => index == 0 ? ("Coś zupełnie innego", 999.99m) : item)
            .Where((_, index) => index != 1)
            .Select(item => new ReceiptItemCommand(item.Name, null, null, null, item.Total, BoundingBox.TryCreate(0.27, 0.19, 0.4, 0.05)))
            .ToList();
        await using (var scope = application.Services.CreateAsyncScope())
        {
            var view = await ProcessingService(scope).ReviewAsync(
                new ReviewReceiptCommand(receiptId, "Lidl", null, "PLN", 136.99m, edited),
                Cancel);
            Assert.Equal(ReceiptBoxSource.Recognition, view.Items[0].BoxSource);
            Assert.Null(view.Items[0].TextPolygons);
            Assert.NotNull(view.Items[0].BoundingBox);
            Assert.All(view.Items.Skip(1), item => Assert.Equal(ReceiptBoxSource.Azure, item.BoxSource));
        }

        await using var readScope = application.Services.CreateAsyncScope();
        var stored = (await readScope.ServiceProvider.GetRequiredService<IReceiptCaptureRepository>().FindByIdAsync(receiptId, Cancel))!;
        Assert.Equal(edited.Count, stored.Items.Count(item => item.BoxSource == ReceiptBoxSource.Azure) + 1);
    }

    [Fact]
    public async Task ASavedPurchaseShowsAndKeepsTheOutlinesThroughACorrection()
    {
        await using var application = await TestApplication.CreateAsync();
        var items = Receipt04Items();
        var receiptId = await AddReceiptAsync(application, items);
        await RunAsync(application, new FakeLocator());
        Guid purchaseId;
        await using (var scope = application.Services.CreateAsyncScope())
        {
            purchaseId = (await ProcessingService(scope).SaveAsync(new SaveReceiptCommand(receiptId), Cancel)).PurchaseId;
        }

        PurchaseDetailView detail;
        await using (var scope = application.Services.CreateAsyncScope())
        {
            detail = await EditService(scope).GetAsync(purchaseId, Cancel);
        }

        Assert.All(detail.Items, item => Assert.Equal(ReceiptBoxSource.Azure, item.BoxSource));
        var firstOutline = detail.Items[1].TextPolygons;

        // A separate request, like the app: drop the first line, so positions
        // shift and the outlines must follow their lines.
        await using (var scope = application.Services.CreateAsyncScope())
        {
            var corrected = await EditService(scope).UpdateAsync(
                new EditPurchaseCommand(
                    purchaseId,
                    detail.UpdatedAt,
                    detail.MerchantName,
                    detail.PurchasedAt,
                    detail.Currency,
                    detail.ReceiptTotal,
                    detail.Items.Skip(1)
                        .Select(item => new EditPurchaseItemCommand(item.Name, item.Quantity, item.Unit, item.UnitPrice, item.LineTotal, item.Kind, BoundingBox: item.BoundingBox, LineId: item.Id))
                        .ToList()),
                Cancel);

            Assert.Equal(detail.Items.Count - 1, corrected.Items.Count);
            Assert.All(corrected.Items, item => Assert.Equal(ReceiptBoxSource.Azure, item.BoxSource));
            Assert.Equal(firstOutline, corrected.Items[0].TextPolygons);
        }
    }

    [Fact]
    public async Task WithoutConfigurationNothingIsSentAndNothingChanges()
    {
        await using var application = await TestApplication.CreateAsync();
        var receiptId = await AddReceiptAsync(application, Receipt04Items());
        var locator = new FakeLocator { Configured = false };

        var run = await RunAsync(application, locator);

        Assert.Equal(ReceiptTextLocationOutcome.Disabled, run.Outcome);
        Assert.Equal(0, locator.Calls);
        await using var scope = application.Services.CreateAsyncScope();
        var receipt = (await scope.ServiceProvider.GetRequiredService<IReceiptCaptureRepository>().FindByIdAsync(receiptId, Cancel))!;
        Assert.Equal(ReceiptTextLocationStatus.NotRequested, receipt.TextLocationStatus);
        Assert.All(receipt.Items, item => Assert.Equal(ReceiptBoxSource.Recognition, item.BoxSource));
    }

    [Fact]
    public async Task SwitchedOffInOptionsNothingIsSent()
    {
        await using var application = await TestApplication.CreateAsync();
        await AddReceiptAsync(application, Receipt04Items());
        var locator = new FakeLocator();

        var run = await RunAsync(application, locator, new ReceiptTextLocationOptions { Enabled = false });

        Assert.Equal(ReceiptTextLocationOutcome.Disabled, run.Outcome);
        Assert.Equal(0, locator.Calls);
    }

    [Fact]
    public async Task AReceiptWithSeveralPhotosIsSkippedWithoutACall()
    {
        await using var application = await TestApplication.CreateAsync();
        var receiptId = await AddReceiptAsync(application, Receipt04Items(), photos: 2);
        var locator = new FakeLocator();

        var first = await RunAsync(application, locator);
        var second = await RunAsync(application, locator);

        Assert.Equal(ReceiptTextLocationOutcome.Skipped, first.Outcome);
        Assert.Equal(ReceiptTextLocationOutcome.Idle, second.Outcome);
        Assert.Equal(0, locator.Calls);
        Assert.Equal(ReceiptTextLocationStatus.Unavailable, await StatusAsync(application, receiptId));
    }

    [Fact]
    public async Task APhotoTooLargeForTheServiceIsSkipped()
    {
        await using var application = await TestApplication.CreateAsync();
        var receiptId = await AddReceiptAsync(application, Receipt04Items());
        var locator = new FakeLocator { MaximumImageBytes = 2 };

        var run = await RunAsync(application, locator);

        Assert.Equal(ReceiptTextLocationOutcome.Skipped, run.Outcome);
        Assert.Equal(0, locator.Calls);
        Assert.Equal(ReceiptTextLocationStatus.Unavailable, await StatusAsync(application, receiptId));
    }

    [Fact]
    public async Task APassingFailureIsRetriedAfterTheDelayAndGivenUpAfterThreeCalls()
    {
        await using var application = await TestApplication.CreateAsync();
        var receiptId = await AddReceiptAsync(application, Receipt04Items());
        var locator = new FakeLocator { Failure = ReceiptTextLocatorFailure.Transient };
        var clock = new MovableClock(Now);

        Assert.Equal(ReceiptTextLocationOutcome.Failed, (await RunAsync(application, locator, clock: clock)).Outcome);
        Assert.Equal(ReceiptTextLocationOutcome.Idle, (await RunAsync(application, locator, clock: clock)).Outcome);
        clock.Advance(TimeSpan.FromMinutes(61));
        Assert.Equal(ReceiptTextLocationOutcome.Failed, (await RunAsync(application, locator, clock: clock)).Outcome);
        clock.Advance(TimeSpan.FromMinutes(61));
        Assert.Equal(ReceiptTextLocationOutcome.Failed, (await RunAsync(application, locator, clock: clock)).Outcome);
        clock.Advance(TimeSpan.FromMinutes(61));
        Assert.Equal(ReceiptTextLocationOutcome.Idle, (await RunAsync(application, locator, clock: clock)).Outcome);

        Assert.Equal(3, locator.Calls);
        Assert.Equal(ReceiptTextLocationStatus.Unavailable, await StatusAsync(application, receiptId));
    }

    [Fact]
    public async Task ARejectedPhotoIsNeverSentAgain()
    {
        await using var application = await TestApplication.CreateAsync();
        var receiptId = await AddReceiptAsync(application, Receipt04Items());
        var locator = new FakeLocator { Failure = ReceiptTextLocatorFailure.Rejected };
        var clock = new MovableClock(Now);

        await RunAsync(application, locator, clock: clock);
        clock.Advance(TimeSpan.FromDays(1));
        var again = await RunAsync(application, locator, clock: clock);

        Assert.Equal(ReceiptTextLocationOutcome.Idle, again.Outcome);
        Assert.Equal(1, locator.Calls);
        Assert.Equal(ReceiptTextLocationStatus.Unavailable, await StatusAsync(application, receiptId));
    }

    [Fact]
    public async Task RateLimitAndRefusedKeyPauseTheWorker()
    {
        await using var application = await TestApplication.CreateAsync();
        await AddReceiptAsync(application, Receipt04Items());
        await AddReceiptAsync(application, Receipt04Items(), createdAt: Now.AddMinutes(-5));

        var limited = await RunAsync(application, new FakeLocator
        {
            Failure = ReceiptTextLocatorFailure.RateLimited,
            RetryAfter = TimeSpan.FromSeconds(142)
        });
        var refused = await RunAsync(application, new FakeLocator { Failure = ReceiptTextLocatorFailure.NotAuthorized });

        Assert.Equal(ReceiptTextLocationOutcome.Paused, limited.Outcome);
        Assert.Equal(TimeSpan.FromSeconds(142), limited.Wait);
        Assert.Equal(ReceiptTextLocationOutcome.Paused, refused.Outcome);
        Assert.Equal(TimeSpan.FromMinutes(360), refused.Wait);
    }

    [Fact]
    public async Task ALocatorThatThrowsAfterTheCallIsCountedAndGivenUpAfterThreeCalls()
    {
        await using var application = await TestApplication.CreateAsync();
        var receiptId = await AddReceiptAsync(application, Receipt04Items());
        var locator = new FakeLocator { Throws = new InvalidOperationException("bug after the call") };
        var clock = new MovableClock(Now);

        var first = await RunAsync(application, locator, clock: clock);
        var immediately = await RunAsync(application, locator, clock: clock);

        Assert.Equal(ReceiptTextLocationOutcome.Failed, first.Outcome);
        Assert.Equal(ReceiptTextLocationOutcome.Idle, immediately.Outcome);
        Assert.Equal((ReceiptTextLocationStatus.Failed, 1, 1), await StateAsync(application, receiptId));
        for (var call = 0; call < 3; call++)
        {
            clock.Advance(TimeSpan.FromMinutes(61));
            await RunAsync(application, locator, clock: clock);
        }

        Assert.Equal(3, locator.Calls);
        Assert.Equal((ReceiptTextLocationStatus.Unavailable, 3, 3), await StateAsync(application, receiptId));
    }

    [Fact]
    public async Task AFailureWhileRecordingTheAnswerStillCountsTheCall()
    {
        await using var application = await TestApplication.CreateAsync();
        var receiptId = await AddReceiptAsync(application, Receipt04Items());
        // A source longer than the column allows makes storing the layout throw after a good answer.
        var locator = new FakeLocator { Source = new string('x', 300) };

        var run = await RunAsync(application, locator);
        var again = await RunAsync(application, locator);

        Assert.Equal(ReceiptTextLocationOutcome.Failed, run.Outcome);
        Assert.Equal(ReceiptTextLocationOutcome.Idle, again.Outcome);
        Assert.Equal(1, locator.Calls);
        Assert.Equal((ReceiptTextLocationStatus.Failed, 1, 1), await StateAsync(application, receiptId));
    }

    [Fact]
    public async Task CallsThatThrowNeverExceedTheMonthlyBudget()
    {
        await using var application = await TestApplication.CreateAsync();
        var options = new ReceiptTextLocationOptions { MonthlyCallLimit = 2, BackfillMonthlyCallLimit = 2 };
        var locator = new FakeLocator { Throws = new IOException("broken answer") };
        for (var index = 0; index < 4; index++)
        {
            await AddReceiptAsync(application, Receipt04Items(), createdAt: Now.AddMinutes(-1 - index));
        }

        var outcomes = new List<ReceiptTextLocationOutcome>();
        for (var run = 0; run < 4; run++)
        {
            outcomes.Add((await RunAsync(application, locator, options)).Outcome);
        }

        Assert.Equal(2, locator.Calls);
        Assert.Equal(
            [ReceiptTextLocationOutcome.Failed, ReceiptTextLocationOutcome.Failed, ReceiptTextLocationOutcome.BudgetExhausted, ReceiptTextLocationOutcome.BudgetExhausted],
            outcomes);
    }

    [Theory]
    [InlineData(ReceiptTextLocatorFailure.RateLimited)]
    [InlineData(ReceiptTextLocatorFailure.NotAuthorized)]
    public async Task AServiceOutageDoesNotUseUpTheReceiptsTriesButCountsTowardTheBudget(ReceiptTextLocatorFailure failure)
    {
        await using var application = await TestApplication.CreateAsync();
        var receiptId = await AddReceiptAsync(application, Receipt04Items());
        var locator = new FakeLocator { Failure = failure };
        var options = new ReceiptTextLocationOptions { MonthlyCallLimit = 5, BackfillMonthlyCallLimit = 5 };

        var outcomes = new List<ReceiptTextLocationOutcome>();
        for (var run = 0; run < 6; run++)
        {
            outcomes.Add((await RunAsync(application, locator, options)).Outcome);
        }

        Assert.Equal(Enumerable.Repeat(ReceiptTextLocationOutcome.Paused, 5).Append(ReceiptTextLocationOutcome.BudgetExhausted), outcomes);
        Assert.Equal(5, locator.Calls);
        Assert.Equal((ReceiptTextLocationStatus.NotRequested, 0, 5), await StateAsync(application, receiptId));

        // Once the service works again, the receipt is located normally.
        var fixedLocator = new FakeLocator();
        var afterFix = await RunAsync(application, fixedLocator, new ReceiptTextLocationOptions());
        Assert.Equal(ReceiptTextLocationOutcome.Located, afterFix.Outcome);
    }

    [Fact]
    public async Task AReviewSavedBeforeTheLayoutStillGetsTheOutlines()
    {
        await using var application = await TestApplication.CreateAsync();
        var items = Receipt04Items();
        var receiptId = await AddReceiptAsync(application, items);

        // The user's screen loaded the receipt before the worker committed "Located".
        await using var userScope = application.Services.CreateAsyncScope();
        var stale = (await userScope.ServiceProvider.GetRequiredService<IReceiptCaptureRepository>().FindByIdAsync(receiptId, Cancel))!;
        Assert.Equal(ReceiptTextLocationStatus.NotRequested, stale.TextLocationStatus);
        await RunAsync(application, new FakeLocator());

        await userScope.ServiceProvider.GetRequiredService<IReceiptTextHighlighter>().RefreshAsync(stale, Cancel);

        Assert.All(stale.Items, item => Assert.Equal(ReceiptBoxSource.Azure, item.BoxSource));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(null)]
    public async Task ARateLimitPausesAtLeastAMinuteWhateverRetryAfterSays(int? retryAfterSeconds)
    {
        await using var application = await TestApplication.CreateAsync();
        await AddReceiptAsync(application, Receipt04Items());
        var run = await RunAsync(application, new FakeLocator
        {
            Failure = ReceiptTextLocatorFailure.RateLimited,
            RetryAfter = retryAfterSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null
        });

        Assert.Equal(ReceiptTextLocationOutcome.Paused, run.Outcome);
        Assert.Equal(TimeSpan.FromMinutes(1), run.Wait);
    }

    [Fact]
    public async Task AFailedReceiptThatUsedItsLastTryIsNeverOfferedAgain()
    {
        await using var application = await TestApplication.CreateAsync();
        var receiptId = await AddReceiptAsync(application, Receipt04Items());
        // As left by a crash during the last allowed call: Failed, tries used up.
        await using (var scope = application.Services.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IReceiptCaptureRepository>();
            var receipt = (await repository.FindByIdAsync(receiptId, Cancel))!;
            for (var call = 0; call < 3; call++)
            {
                receipt.BeginTextLocationCall(Now.AddDays(-1));
            }

            await repository.SaveChangesAsync(Cancel);
        }

        var locator = new FakeLocator();
        var run = await RunAsync(application, locator);

        Assert.Equal(ReceiptTextLocationOutcome.Idle, run.Outcome);
        Assert.Equal(0, locator.Calls);
        Assert.Equal((ReceiptTextLocationStatus.Failed, 3, 3), await StateAsync(application, receiptId));
    }

    [Fact]
    public async Task NewestReceiptGoesFirst()
    {
        await using var application = await TestApplication.CreateAsync();
        var older = await AddReceiptAsync(application, Receipt04Items(), createdAt: Now.AddDays(-30));
        var newer = await AddReceiptAsync(application, Receipt04Items(), createdAt: Now.AddMinutes(-1));
        var locator = new FakeLocator();

        Assert.Equal(newer, (await RunAsync(application, locator)).ReceiptId);
        Assert.Equal(older, (await RunAsync(application, locator)).ReceiptId);
    }

    [Fact]
    public async Task TheMonthlyBudgetStopsCallsAndKeepsTheRestForNewReceipts()
    {
        await using var application = await TestApplication.CreateAsync();
        var locator = new FakeLocator();
        var options = new ReceiptTextLocationOptions { MonthlyCallLimit = 2, BackfillMonthlyCallLimit = 1 };
        await AddReceiptAsync(application, Receipt04Items(), createdAt: Now.AddDays(-40));
        await AddReceiptAsync(application, Receipt04Items(), createdAt: Now.AddDays(-41));

        // First old receipt fits the backfill share, the second does not.
        Assert.Equal(ReceiptTextLocationOutcome.Located, (await RunAsync(application, locator, options)).Outcome);
        Assert.Equal(ReceiptTextLocationOutcome.BudgetExhausted, (await RunAsync(application, locator, options)).Outcome);

        // A new receipt still gets the remaining call; after that nothing goes out.
        var fresh = await AddReceiptAsync(application, Receipt04Items(), createdAt: Now.AddMinutes(-2));
        Assert.Equal(fresh, (await RunAsync(application, locator, options)).ReceiptId);
        await AddReceiptAsync(application, Receipt04Items(), createdAt: Now.AddMinutes(-1));
        Assert.Equal(ReceiptTextLocationOutcome.BudgetExhausted, (await RunAsync(application, locator, options)).Outcome);
        Assert.Equal(2, locator.Calls);
    }

    [Fact]
    public async Task FinishedRecognitionRingsTheDoorbell()
    {
        await using var application = await TestApplication.CreateAsync();
        var receiptId = Guid.NewGuid();
        await using var scope = application.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IReceiptCaptureRepository>();
        var receipt = ReceiptCapture.CreateUploaded(receiptId, [Part(receiptId, 0)], Now);
        receipt.Queue(Now);
        await repository.AddOrFindExactDuplicateAsync(receipt, Cancel);
        var signal = new CountingSignal();
        var service = new ReceiptProcessingService(
            repository,
            new PhotoStore(anyPurpose: true),
            new OneLineExtractor(),
            new UnusedDispatcher(),
            scope.ServiceProvider.GetRequiredService<IPurchaseDraftService>(),
            scope.ServiceProvider.GetRequiredService<TimeProvider>(),
            scope.ServiceProvider.GetRequiredService<IReceiptTextHighlighter>(),
            signal);

        await service.ProcessAsync(receiptId, Cancel);

        Assert.Equal(1, signal.Count);
    }

    [Fact]
    public async Task TheDoorbellCollapsesRingsAndTimesOut()
    {
        var signal = new PurchaseMemory.Infrastructure.Processing.ChannelReceiptTextLocationSignal();
        signal.Notify();
        signal.Notify();
        await signal.WaitAsync(TimeSpan.FromSeconds(5), Cancel);
        var started = System.Diagnostics.Stopwatch.StartNew();
        await signal.WaitAsync(TimeSpan.FromMilliseconds(50), Cancel);
        Assert.True(started.ElapsedMilliseconds >= 40);
    }

    [Fact]
    public async Task AReceiptStillInRecognitionIsNotACandidate()
    {
        await using var application = await TestApplication.CreateAsync();
        await using (var scope = application.Services.CreateAsyncScope())
        {
            var receipt = ReceiptCapture.CreateUploaded(Guid.NewGuid(), [Part(Guid.NewGuid(), 0)], Now);
            receipt.Queue(Now);
            await scope.ServiceProvider.GetRequiredService<IReceiptCaptureRepository>().AddOrFindExactDuplicateAsync(receipt, Cancel);
        }

        var locator = new FakeLocator();
        Assert.Equal(ReceiptTextLocationOutcome.Idle, (await RunAsync(application, locator)).Outcome);
        Assert.Equal(0, locator.Calls);
    }

    private static List<(string Name, decimal? Total)> Receipt04Items() =>
        ReceiptTextMatcherTests.LoadHaikuItems("04").Select(item => (item.Name, item.LineTotal)).ToList();

    private static async Task<ReceiptTextLocationRun> RunAsync(
        TestApplication application,
        FakeLocator locator,
        ReceiptTextLocationOptions? options = null,
        TimeProvider? clock = null)
    {
        await using var scope = application.Services.CreateAsyncScope();
        var service = new ReceiptTextLocationService(
            locator,
            scope.ServiceProvider.GetRequiredService<IReceiptTextLayoutRepository>(),
            scope.ServiceProvider.GetRequiredService<IReceiptCaptureRepository>(),
            new PhotoStore(),
            options ?? new ReceiptTextLocationOptions(),
            clock ?? new MovableClock(Now),
            NullLogger<ReceiptTextLocationService>.Instance);
        return await service.LocateNextAsync(Cancel);
    }

    private static async Task<Guid> AddReceiptAsync(
        TestApplication application,
        IReadOnlyList<(string Name, decimal? Total)> items,
        int photos = 1,
        DateTimeOffset? createdAt = null)
    {
        var id = Guid.NewGuid();
        var created = createdAt ?? Now.AddMinutes(-10);
        var receipt = ReceiptCapture.CreateUploaded(id, Enumerable.Range(0, photos).Select(position => Part(id, position)).ToList(), created);
        receipt.Queue(created);
        receipt.StartProcessing(created);
        receipt.CompleteProcessing(
            "Lidl",
            null,
            "PLN",
            136.99m,
            items.Select((item, position) => ReceiptCaptureItem.Create(
                position,
                item.Name,
                null,
                null,
                null,
                item.Total,
                BoundingBox.TryCreate(0.27, 0.19, 0.4, 0.05),
                item.Total < 0 ? ReceiptLineKind.Discount : ReceiptLineKind.Product)),
            "fake",
            "fake-model",
            created);
        await using var scope = application.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IReceiptCaptureRepository>().AddOrFindExactDuplicateAsync(receipt, Cancel);
        return id;
    }

    private static ReceiptImagePart Part(Guid receiptId, int position) =>
        ReceiptImagePart.Create(
            position,
            $"{receiptId:N}-{position}.jpg",
            "image/jpeg",
            3,
            Convert.ToHexString(SHA256.HashData(receiptId.ToByteArray().Append((byte)position).ToArray())).ToLowerInvariant());

    private static async Task<(ReceiptTextLocationStatus Status, int Attempts, int Calls)> StateAsync(TestApplication application, Guid receiptId)
    {
        await using var scope = application.Services.CreateAsyncScope();
        var receipt = (await scope.ServiceProvider.GetRequiredService<IReceiptCaptureRepository>().FindByIdAsync(receiptId, Cancel))!;
        return (receipt.TextLocationStatus, receipt.TextLocationAttempts, receipt.TextLocationCalls);
    }

    private static async Task<ReceiptTextLocationStatus> StatusAsync(TestApplication application, Guid receiptId)
    {
        await using var scope = application.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<IReceiptCaptureRepository>().FindByIdAsync(receiptId, Cancel))!.TextLocationStatus;
    }

    private static ReceiptProcessingService ProcessingService(AsyncServiceScope scope) =>
        new(
            scope.ServiceProvider.GetRequiredService<IReceiptCaptureRepository>(),
            new PhotoStore(),
            new UnusedExtractor(),
            new UnusedDispatcher(),
            scope.ServiceProvider.GetRequiredService<IPurchaseDraftService>(),
            scope.ServiceProvider.GetRequiredService<TimeProvider>(),
            scope.ServiceProvider.GetRequiredService<IReceiptTextHighlighter>());

    private static PurchaseEditService EditService(AsyncServiceScope scope) =>
        new(
            scope.ServiceProvider.GetRequiredService<IPurchaseDraftRepository>(),
            scope.ServiceProvider.GetRequiredService<IProductCatalogRepository>(),
            scope.ServiceProvider.GetRequiredService<IReceiptCaptureRepository>(),
            scope.ServiceProvider.GetRequiredService<IProductMatcher>(),
            scope.ServiceProvider.GetRequiredService<IProductCategorizationDispatcher>(),
            scope.ServiceProvider.GetRequiredService<TimeProvider>(),
            scope.ServiceProvider.GetRequiredService<IReceiptTextHighlighter>());

    private sealed class FakeLocator : IReceiptTextLocator
    {
        public bool Configured { get; init; } = true;

        public ReceiptTextLocatorFailure? Failure { get; init; }

        /// <summary>Thrown from the call, after the request would have gone out.</summary>
        public Exception? Throws { get; init; }

        public string Source { get; init; } = "azure:prebuilt-read:test";

        public TimeSpan? RetryAfter { get; init; }

        public int Calls { get; private set; }

        public bool IsConfigured => Configured;

        public long MaximumImageBytes { get; init; } = 4 * 1024 * 1024;

        public Task<ReceiptTextLocatorResult> ReadAsync(ReadOnlyMemory<byte> image, string contentType, CancellationToken cancellationToken)
        {
            Calls++;
            if (Throws is not null)
            {
                throw Throws;
            }

            if (Failure is { } failure)
            {
                return Task.FromResult(ReceiptTextLocatorResult.Failed(failure, RetryAfter));
            }

            var layout = ReceiptTextMatcherTests.LoadLayout("04");
            return Task.FromResult(ReceiptTextLocatorResult.Read(
                new ReceiptTextPage(Source, layout.PageWidth, layout.PageHeight, layout.Lines)));
        }
    }

    /// <summary>Every photo is three bytes; text location may only ask for the viewing copy.</summary>
    private sealed class PhotoStore(bool anyPurpose = false) : IReceiptImageStore
    {
        public Task<StoredReceiptImage> StoreAsync(Guid receiptId, int position, Stream image, string declaredContentType, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Text location reads the viewing copy.");

        public Task<ReceiptImageContent> OpenReadAsync(string storageKey, ReceiptImagePurpose purpose, CancellationToken cancellationToken)
        {
            Assert.True(anyPurpose || purpose == ReceiptImagePurpose.Viewing);
            return Task.FromResult(new ReceiptImageContent(new MemoryStream([1, 2, 3]), "image/jpeg"));
        }

        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task DeleteReceiptAsync(Guid receiptId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class UnusedExtractor : IReceiptExtractor
    {
        public Task<ReceiptExtractionResult> ExtractAsync(IReadOnlyList<ReceiptImageData> images, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class OneLineExtractor : IReceiptExtractor
    {
        public Task<ReceiptExtractionResult> ExtractAsync(IReadOnlyList<ReceiptImageData> images, CancellationToken cancellationToken) =>
            Task.FromResult(new ReceiptExtractionResult(
                "Lidl", null, "PLN", 4.49m, [new ReceiptExtractionItem("MLEKO", 1, null, 4.49m, 4.49m)], "fake", "fake-model"));
    }

    private sealed class CountingSignal : IReceiptTextLocationSignal
    {
        public int Count { get; private set; }

        public void Notify() => Count++;
    }

    private sealed class UnusedDispatcher : IReceiptProcessingDispatcher
    {
        public ValueTask EnqueueAsync(Guid receiptId, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class MovableClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
