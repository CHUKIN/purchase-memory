using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PurchaseMemory.Api.Endpoints;
using PurchaseMemory.Application.Purchases;
using PurchaseMemory.Application.Receipts;
using PurchaseMemory.Domain.Receipts;

namespace PurchaseMemory.IntegrationTests;

/// <summary>
/// Covers the whole best-effort bounding box path: model output, validation,
/// persistence, issue detection and the JSON the PWA receives.
/// </summary>
public sealed class ReceiptBoundingBoxPipelineTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void ParserKeepsAValidBoxForItemsAndForTheTotal()
    {
        var parsed = ReceiptStructuredOutputParser.Parse(
            """
            {
              "merchantName": "Biedronka",
              "purchasedAt": null,
              "currency": "PLN",
              "receiptTotal": 6.49,
              "receiptTotalBoundingBox": {"x": 0.05, "y": 0.86, "width": 0.6, "height": 0.04},
              "items": [
                {
                  "name": "MLEKO",
                  "quantity": 1,
                  "unit": "szt",
                  "unitPrice": 6.49,
                  "lineTotal": 6.49,
                  "boundingBox": {"x": 0.08, "y": 0.42, "width": 0.75, "height": 0.03}
                }
              ]
            }
            """);

        var item = Assert.Single(parsed.Extraction.Items);
        Assert.NotNull(item.BoundingBox);
        Assert.Equal(0.08, item.BoundingBox.X, 6);
        Assert.Equal(0.03, item.BoundingBox.Height, 6);
        Assert.NotNull(parsed.Extraction.ReceiptTotalBoundingBox);
        Assert.Equal(0.86, parsed.Extraction.ReceiptTotalBoundingBox.Y, 6);
    }

    [Theory]
    // Outside the image, degenerate, the whole frame, a wrong JSON type, a
    // missing corner and an explicit null: every one of them must be dropped
    // without rejecting the recognized values.
    [InlineData("""{"x": 0.8, "y": 0.4, "width": 0.5, "height": 0.03}""")]
    [InlineData("""{"x": 0.1, "y": 0.4, "width": 0, "height": 0.03}""")]
    [InlineData("""{"x": 0, "y": 0, "width": 1, "height": 1}""")]
    [InlineData("""{"x": "0.1", "y": 0.4, "width": 0.5, "height": 0.03}""")]
    [InlineData("""{"x": 0.1, "y": 0.4, "width": 0.5}""")]
    [InlineData("\"somewhere in the middle\"")]
    [InlineData("[0.1, 0.4, 0.5, 0.03]")]
    [InlineData("null")]
    public void ParserDropsAnUnusableBoxButKeepsTheReceipt(string boundingBoxJson)
    {
        var parsed = ReceiptStructuredOutputParser.Parse(
            $$"""
            {
              "merchantName": "Biedronka",
              "purchasedAt": null,
              "currency": "PLN",
              "receiptTotal": 6.49,
              "receiptTotalBoundingBox": {{boundingBoxJson}},
              "items": [
                {
                  "name": "MLEKO",
                  "quantity": 1,
                  "unit": "szt",
                  "unitPrice": 6.49,
                  "lineTotal": 6.49,
                  "boundingBox": {{boundingBoxJson}}
                }
              ]
            }
            """);

        Assert.Equal("MLEKO", Assert.Single(parsed.Extraction.Items).Name);
        Assert.Null(parsed.Extraction.Items[0].BoundingBox);
        Assert.Null(parsed.Extraction.ReceiptTotalBoundingBox);
    }

    [Fact]
    public void ParserStillAcceptsAResponseWithoutAnyBoxes()
    {
        var parsed = ReceiptStructuredOutputParser.Parse(
            """
            {
              "merchantName": null,
              "purchasedAt": null,
              "currency": "PLN",
              "receiptTotal": 6.49,
              "items": [{"name":"MLEKO","quantity":1,"unit":"szt","unitPrice":6.49,"lineTotal":6.49}]
            }
            """);

        Assert.Null(Assert.Single(parsed.Extraction.Items).BoundingBox);
        Assert.Null(parsed.Extraction.ReceiptTotalBoundingBox);
    }

    [Fact]
    public void DetectorAttachesTheBoxOfTheItemAndOfTheTotalToTheirIssues()
    {
        var itemBox = BoundingBox.TryCreate(0.08, 0.42, 0.75, 0.03);
        var totalBox = BoundingBox.TryCreate(0.05, 0.86, 0.6, 0.04);

        var issues = PurchaseDraftIssueDetector.Detect(
            99m,
            "PLN",
            [
                new PurchaseDraftIssueItem(0, "MLEKO", 6.49m, 6.49m),
                // A line total without a unit price: the item is still an issue,
                // and every line total is known, so the totals can be compared.
                new PurchaseDraftIssueItem(1, "CHLEB", null, 3.51m, itemBox)
            ],
            totalBox);

        var missingPrice = Assert.Single(issues, issue => issue.Type == PurchaseDraftIssueType.MissingItemPrice);
        Assert.Same(itemBox, missingPrice.BoundingBox);
        var mismatch = Assert.Single(issues, issue => issue.Type == PurchaseDraftIssueType.ReceiptTotalMismatch);
        Assert.Same(totalBox, mismatch.BoundingBox);
    }

    [Fact]
    public void DetectorLeavesTheBoxNullWhenRecognitionDidNotReturnOne()
    {
        var issues = PurchaseDraftIssueDetector.Detect(
            10m,
            "PLN",
            [new PurchaseDraftIssueItem(0, "MLEKO", null, null)]);

        Assert.Null(Assert.Single(issues).BoundingBox);
    }

    [Fact]
    public async Task RecognizedBoxesSurvivePersistenceAndReachTheIssueOfTheView()
    {
        await using var application = await TestApplication.CreateAsync();
        await using var scope = application.Services.CreateAsyncScope();
        var service = new ReceiptProcessingService(
            scope.ServiceProvider.GetRequiredService<IReceiptCaptureRepository>(),
            new InMemoryReceiptImageStore(),
            new BoxedReceiptExtractor(),
            new NoOpDispatcher(),
            scope.ServiceProvider.GetRequiredService<IPurchaseDraftService>(),
            scope.ServiceProvider.GetRequiredService<TimeProvider>());

        var uploaded = await UploadAsync(service);
        await service.QueueAsync(uploaded.ReceiptId, TestContext.Current.CancellationToken);
        await service.ProcessAsync(uploaded.ReceiptId, TestContext.Current.CancellationToken);

        // A second scope proves the box came from the database, not from the
        // change tracker of the context that wrote it.
        await using var readScope = application.Services.CreateAsyncScope();
        var readService = new ReceiptProcessingService(
            readScope.ServiceProvider.GetRequiredService<IReceiptCaptureRepository>(),
            new InMemoryReceiptImageStore(),
            new BoxedReceiptExtractor(),
            new NoOpDispatcher(),
            readScope.ServiceProvider.GetRequiredService<IPurchaseDraftService>(),
            readScope.ServiceProvider.GetRequiredService<TimeProvider>());
        var view = await readService.GetAsync(uploaded.ReceiptId, TestContext.Current.CancellationToken);

        Assert.Null(view.Items[0].BoundingBox);
        var itemBox = view.Items[1].BoundingBox;
        Assert.NotNull(itemBox);
        Assert.Equal(0.42, itemBox.Y, 6);
        Assert.NotNull(view.ReceiptTotalBoundingBox);

        var missingPrice = Assert.Single(
            view.Issues,
            issue => issue.Type == PurchaseDraftIssueType.MissingItemPrice);
        var issueBox = missingPrice.BoundingBox;
        Assert.NotNull(issueBox);
        Assert.Equal(0.08, issueBox.X, 6);
    }

    [Fact]
    public async Task ReviewDropsAClientBoxThatDoesNotValidateAndKeepsAValidOne()
    {
        await using var application = await TestApplication.CreateAsync();
        await using var scope = application.Services.CreateAsyncScope();
        var service = new ReceiptProcessingService(
            scope.ServiceProvider.GetRequiredService<IReceiptCaptureRepository>(),
            new InMemoryReceiptImageStore(),
            new BoxedReceiptExtractor(),
            new NoOpDispatcher(),
            scope.ServiceProvider.GetRequiredService<IPurchaseDraftService>(),
            scope.ServiceProvider.GetRequiredService<TimeProvider>());

        var uploaded = await UploadAsync(service);
        await service.QueueAsync(uploaded.ReceiptId, TestContext.Current.CancellationToken);
        await service.ProcessAsync(uploaded.ReceiptId, TestContext.Current.CancellationToken);

        // The request DTO is what the PWA actually sends, so the mapping the
        // endpoint uses is exercised here too.
        var request = new ReviewReceiptRequest(
            "Biedronka",
            null,
            "PLN",
            10m,
            [
                new ReviewReceiptItemRequest(
                    "MLEKO",
                    1,
                    "szt",
                    6.49m,
                    6.49m,
                    new BoundingBoxPayload(0.9, 0.4, 0.5, 0.03)),
                new ReviewReceiptItemRequest(
                    "CHLEB",
                    1,
                    "szt",
                    3.51m,
                    3.51m,
                    new BoundingBoxPayload(0.08, 0.42, 0.75, 0.03))
            ],
            new BoundingBoxPayload(0.05, 0.86, 0.6, 0.04));

        var reviewed = await service.ReviewAsync(
            request.ToCommand(uploaded.ReceiptId),
            TestContext.Current.CancellationToken);

        Assert.Null(reviewed.Items[0].BoundingBox);
        var keptBox = reviewed.Items[1].BoundingBox;
        Assert.NotNull(keptBox);
        Assert.Equal(0.75, keptBox.Width, 6);
        var totalBox = reviewed.ReceiptTotalBoundingBox;
        Assert.NotNull(totalBox);
        Assert.Equal(0.86, totalBox.Y, 6);
    }

    [Fact]
    public async Task ApiResponseExposesTheBoxAsCamelCaseFractionsOnIssuesAndSnapshot()
    {
        await using var application = await TestApplication.CreateAsync();
        await using var scope = application.Services.CreateAsyncScope();
        var service = new ReceiptProcessingService(
            scope.ServiceProvider.GetRequiredService<IReceiptCaptureRepository>(),
            new InMemoryReceiptImageStore(),
            new BoxedReceiptExtractor(),
            new NoOpDispatcher(),
            scope.ServiceProvider.GetRequiredService<IPurchaseDraftService>(),
            scope.ServiceProvider.GetRequiredService<TimeProvider>());

        var uploaded = await UploadAsync(service);
        await service.QueueAsync(uploaded.ReceiptId, TestContext.Current.CancellationToken);
        var processed = await service.ProcessAsync(uploaded.ReceiptId, TestContext.Current.CancellationToken);

        using var json = JsonDocument.Parse(
            JsonSerializer.Serialize(ReceiptResponse.FromView(processed), JsonOptions));
        var root = json.RootElement;

        var issue = root.GetProperty("issues")
            .EnumerateArray()
            .Single(candidate => candidate.GetProperty("type").GetString() == "missingItemPrice");
        var box = issue.GetProperty("boundingBox");
        Assert.Equal(0.08, box.GetProperty("x").GetDouble(), 6);
        Assert.Equal(0.42, box.GetProperty("y").GetDouble(), 6);
        Assert.Equal(0.75, box.GetProperty("width").GetDouble(), 6);
        Assert.Equal(0.03, box.GetProperty("height").GetDouble(), 6);

        var items = root.GetProperty("snapshot").GetProperty("items");
        Assert.Equal(JsonValueKind.Null, items[0].GetProperty("boundingBox").ValueKind);
        Assert.Equal(0.08, items[1].GetProperty("boundingBox").GetProperty("x").GetDouble(), 6);
        Assert.Equal(
            0.86,
            root.GetProperty("snapshot").GetProperty("receiptTotalBoundingBox").GetProperty("y").GetDouble(),
            6);
    }

    [Fact]
    public void PurchaseIssueResponseCarriesTheBoxField()
    {
        var response = PurchaseDraftIssueResponse.FromIssue(
            new PurchaseDraftIssue(
                PurchaseDraftIssueType.MissingItemPrice,
                0,
                "MLEKO",
                string.Empty,
                "Set the price for this item or remove it."));

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(response, JsonOptions));

        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("boundingBox").ValueKind);
    }

    private static async Task<ReceiptCaptureView> UploadAsync(ReceiptProcessingService service)
    {
        await using var image = new MemoryStream("fake-image"u8.ToArray());
        var result = await service.UploadAsync(
            new UploadReceiptCommand([new ReceiptImageUpload(image, "image/jpeg")]),
            TestContext.Current.CancellationToken);
        return result.Receipt;
    }

    /// <summary>
    /// Returns one line without a box, one line with a valid box and a total box,
    /// which together produce both issue types.
    /// </summary>
    private sealed class BoxedReceiptExtractor : IReceiptExtractor
    {
        public Task<ReceiptExtractionResult> ExtractAsync(
            IReadOnlyList<ReceiptImageData> images,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new ReceiptExtractionResult(
                "Biedronka",
                null,
                "PLN",
                99m,
                [
                    new ReceiptExtractionItem("MLEKO", 1, "szt", 6.49m, 6.49m),
                    new ReceiptExtractionItem(
                        "CHLEB",
                        1,
                        "szt",
                        null,
                        null,
                        BoundingBox.TryCreate(0.08, 0.42, 0.75, 0.03))
                ],
                "fake",
                "fake-vision",
                BoundingBox.TryCreate(0.05, 0.86, 0.6, 0.04)));
        }
    }

    private sealed class InMemoryReceiptImageStore : IReceiptImageStore
    {
        private readonly Dictionary<string, byte[]> _images = [];

        public async Task<StoredReceiptImage> StoreAsync(
            Guid receiptId,
            int position,
            Stream image,
            string declaredContentType,
            CancellationToken cancellationToken)
        {
            using var buffer = new MemoryStream();
            await image.CopyToAsync(buffer, cancellationToken);
            var bytes = buffer.ToArray();
            var key = $"{receiptId:N}-{position}.jpg";
            _images[key] = bytes;
            return new StoredReceiptImage(
                key,
                "image/jpeg",
                bytes.LongLength,
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant());
        }

        public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken) =>
            Task.FromResult<Stream>(new MemoryStream(_images[storageKey]));

        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
        {
            _images.Remove(storageKey);
            return Task.CompletedTask;
        }

        public Task DeleteReceiptAsync(Guid receiptId, CancellationToken cancellationToken)
        {
            foreach (var key in _images.Keys.Where(key => key.StartsWith($"{receiptId:N}-", StringComparison.Ordinal)).ToList())
            {
                _images.Remove(key);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class NoOpDispatcher : IReceiptProcessingDispatcher
    {
        public ValueTask EnqueueAsync(Guid receiptId, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException("These tests drive processing directly.");
    }
}
