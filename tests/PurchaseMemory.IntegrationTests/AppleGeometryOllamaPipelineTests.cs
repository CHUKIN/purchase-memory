using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PurchaseMemory.Application.Receipts;
using PurchaseMemory.Infrastructure.Recognition;

namespace PurchaseMemory.IntegrationTests;

/// <summary>
/// Covers the "Apple Vision OCR rows → Ollama text parser" route: the prompt and
/// call parameters carried over from research/ocr-geometry/drafts/parse.py, the
/// parsing of the model answer, and every failure that must be loud instead of
/// returning an empty receipt.
/// </summary>
public sealed class AppleGeometryOllamaPipelineTests
{
    private static readonly ReceiptOcrPage Page = new(
        [
            new ReceiptOcrRow(172, ["POLĘDWICA WOŁOWA KGO", "0,446 x157,49 70,24C"]),
            new ReceiptOcrRow(190, ["OPUST", "-0,80"])
        ],
        FragmentCount: 4,
        Skew: -16,
        RowsWithSinglePriceCell: 1,
        RowsWithMultiplePriceCells: 0);

    private const string ValidAnswer = """
        {
          "merchantName": "E.Leclerc",
          "purchasedAt": "2026-09-18T17:42:00+02:00",
          "currency": "PLN",
          "receiptTotal": 69.44,
          "items": [
            { "name": "POLĘDWICA WOŁOWA", "quantity": 0.446, "unit": "kg", "unitPrice": 157.49, "lineTotal": 70.24 },
            { "name": "OPUST", "quantity": null, "unit": null, "unitPrice": null, "lineTotal": -0.80 }
          ]
        }
        """;

    [Fact]
    public void PromptCarriesTheResearchRowFormatAndRules()
    {
        var prompt = ReceiptRowsAnalysisContract.BuildPrompt([Page]);

        Assert.Contains("\"y\": vertical position", prompt, StringComparison.Ordinal);
        Assert.Contains("weighted goods print on TWO rows", prompt, StringComparison.Ordinal);
        Assert.Contains("ignore them", prompt, StringComparison.Ordinal);
        // A single photo must serialize to the bare row array the research script used.
        Assert.Contains(
            """ROWS = [{"y":172,"cells":["POLĘDWICA WOŁOWA KGO","0,446 x157,49 70,24C"]}""",
            prompt,
            StringComparison.Ordinal);
        Assert.DoesNotContain("\"part\":", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void MultiplePhotosAreLabelledAsOrderedPartsOfOneReceipt()
    {
        var prompt = ReceiptRowsAnalysisContract.BuildPrompt([Page, Page]);

        Assert.Contains("ordered parts of ONE receipt", prompt, StringComparison.Ordinal);
        Assert.Contains("""ROWS = [{"part":1,"rows":[""", prompt, StringComparison.Ordinal);
        Assert.Contains("""{"part":2,"rows":[""", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ParserModelAndDeterministicOptionsComeFromConfiguration()
    {
        using var handler = new TextHandler { InstalledModels = ["cloud-parser:tag"] };
        using var provider = CreateProvider(handler, new()
        {
            RowsParserModel = "cloud-parser:tag",
            ContextLength = 32_768,
            // The route has its own output budget; the vision route's value must
            // not leak into it.
            MaximumOutputTokens = 8_192,
            RowsParserMaximumOutputTokens = 16_384
        });

        var response = await provider.GenerateAsync(
            CreateRequest(),
            TestContext.Current.CancellationToken);

        Assert.Equal("cloud-parser:tag", response.Model);
        Assert.Equal("ollama-text", response.Provider);
        var body = handler.SingleBody("/api/chat");
        Assert.Equal("cloud-parser:tag", body.GetProperty("model").GetString());
        Assert.False(body.GetProperty("stream").GetBoolean());
        Assert.False(body.GetProperty("think").GetBoolean());
        Assert.Equal(0, body.GetProperty("options").GetProperty("temperature").GetDouble());
        Assert.Equal(32_768, body.GetProperty("options").GetProperty("num_ctx").GetInt32());
        Assert.Equal(16_384, body.GetProperty("options").GetProperty("num_predict").GetInt32());
        Assert.True(JsonElement.DeepEquals(
            JsonDocument.Parse(ReceiptRowsAnalysisContract.JsonSchema).RootElement,
            body.GetProperty("format")));
        var content = body.GetProperty("messages")[0].GetProperty("content").GetString();
        Assert.Contains("ROWS = ", content, StringComparison.Ordinal);
        Assert.Contains(ReceiptRowsAnalysisContract.JsonSchema, content, StringComparison.Ordinal);
        // A text parser has no images, so nothing image-shaped may leak into the call.
        Assert.False(body.GetProperty("messages")[0].TryGetProperty("images", out _));
    }

    [Fact]
    public async Task WorkflowParsesTheModelAnswerIncludingNegativeDiscountLines()
    {
        using var handler = new TextHandler { InstalledModels = ["parser"], Answer = ValidAnswer };
        using var provider = CreateProvider(handler, new() { RowsParserModel = "parser" });
        var workflow = new ReceiptRowsAnalysisWorkflow(new StructuredTextProviderResolver([provider]));

        var result = await workflow.AnalyzeAsync(
            new ReceiptRowsAnalysisRequest("correlation", [Page], "ollama-text"),
            TestContext.Current.CancellationToken);

        Assert.Equal("E.Leclerc", result.Extraction.MerchantName);
        Assert.Equal(69.44m, result.Extraction.ReceiptTotal);
        Assert.Equal(2, result.Extraction.Items.Count);
        Assert.Equal(0.446m, result.Extraction.Items[0].Quantity);
        Assert.Equal("kg", result.Extraction.Items[0].Unit);
        Assert.Equal(-0.80m, result.Extraction.Items[1].LineTotal);
        Assert.Equal("parser", result.Metadata.Model);
        Assert.Equal(ReceiptRowsAnalysisContract.PromptVersion, result.Metadata.PromptVersion);
    }

    [Fact]
    public async Task UnparsableAnswerIsRetriedOnceAndThenReportedWithItsErrors()
    {
        using var handler = new TextHandler { InstalledModels = ["parser"], Answer = "I cannot read this receipt." };
        using var provider = CreateProvider(handler, new() { RowsParserModel = "parser" });
        var workflow = new ReceiptRowsAnalysisWorkflow(new StructuredTextProviderResolver([provider]));

        var error = await Assert.ThrowsAsync<ReceiptStructuredOutputException>(
            () => workflow.AnalyzeAsync(
                new ReceiptRowsAnalysisRequest("correlation", [Page], "ollama-text"),
                TestContext.Current.CancellationToken));

        Assert.NotEmpty(error.ValidationErrors);
        Assert.Equal(2, handler.Bodies("/api/chat").Length);
        var repairPrompt = handler.Bodies("/api/chat")[1]
            .GetProperty("messages")[0].GetProperty("content").GetString();
        Assert.Contains("Your previous JSON was rejected", repairPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmptyRowsAreRejectedBeforeAskingTheModel()
    {
        using var handler = new TextHandler { InstalledModels = ["parser"] };
        using var provider = CreateProvider(handler, new() { RowsParserModel = "parser" });
        var workflow = new ReceiptRowsAnalysisWorkflow(new StructuredTextProviderResolver([provider]));
        var emptyPage = new ReceiptOcrPage([], 0, 0, 0, 0);

        var error = await Assert.ThrowsAsync<ReceiptExtractionException>(
            () => workflow.AnalyzeAsync(
                new ReceiptRowsAnalysisRequest("correlation", [emptyPage], "ollama-text"),
                TestContext.Current.CancellationToken));

        Assert.Contains("did not return any receipt rows", error.Message, StringComparison.Ordinal);
        Assert.Empty(handler.RequestPaths);
    }

    /// <summary>
    /// Also the regression guard for the capability lock: the provider serializes
    /// its model check on a <see cref="SemaphoreSlim"/>, and a missing release
    /// deadlocks every call after the first one. The deliberately short deadline
    /// makes that failure a red test in seconds instead of a build that never ends.
    /// </summary>
    [Fact(Timeout = 30_000)]
    public async Task MissingParserModelIsReportedWithThePullCommandOnEveryCall()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        using var handler = new TextHandler { InstalledModels = ["something-else:latest"] };
        using var provider = CreateProvider(handler, new() { RowsParserModel = "qwen3.5:35b" });

        var status = await provider.GetStatusAsync(deadline.Token);
        var error = await Assert.ThrowsAsync<ReceiptExtractionException>(
            () => provider.GenerateAsync(CreateRequest(), deadline.Token));

        Assert.False(status.Available);
        Assert.Contains("ollama pull qwen3.5:35b", status.Message, StringComparison.Ordinal);
        Assert.Contains("ollama pull qwen3.5:35b", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(handler.RequestPaths, path => path == "/api/chat");
    }

    [Fact]
    public async Task TruncatedAndEmptyAnswersFailLoudly()
    {
        using var truncatedHandler = new TextHandler
        {
            InstalledModels = ["parser"],
            Answer = "{\"merchantName\":\"E.Le",
            DoneReason = "length"
        };
        using var truncatedProvider = CreateProvider(truncatedHandler, new() { RowsParserModel = "parser" });
        var truncated = await Assert.ThrowsAsync<ReceiptExtractionException>(
            () => truncatedProvider.GenerateAsync(CreateRequest(), TestContext.Current.CancellationToken));
        Assert.Contains("truncated", truncated.Message, StringComparison.Ordinal);

        using var emptyHandler = new TextHandler { InstalledModels = ["parser"], Answer = "   " };
        using var emptyProvider = CreateProvider(emptyHandler, new() { RowsParserModel = "parser" });
        var empty = await Assert.ThrowsAsync<ReceiptExtractionException>(
            () => emptyProvider.GenerateAsync(CreateRequest(), TestContext.Current.CancellationToken));
        Assert.Contains("empty structured result", empty.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnreachableOllamaAndRejectedRequestsAreMappedToSafeMessages()
    {
        using var offlineHandler = new ThrowingHandler(new HttpRequestException("offline"));
        using var offlineProvider = CreateProvider(offlineHandler, new() { RowsParserModel = "parser" });
        var offline = await Assert.ThrowsAsync<ReceiptExtractionException>(
            () => offlineProvider.GenerateAsync(CreateRequest(), TestContext.Current.CancellationToken));
        Assert.Contains("not reachable", offline.Message, StringComparison.Ordinal);

        using var rejectedHandler = new TextHandler
        {
            InstalledModels = ["parser"],
            ChatStatusCode = HttpStatusCode.ServiceUnavailable
        };
        using var rejectedProvider = CreateProvider(rejectedHandler, new() { RowsParserModel = "parser" });
        var rejected = await Assert.ThrowsAsync<ReceiptExtractionException>(
            () => rejectedProvider.GenerateAsync(CreateRequest(), TestContext.Current.CancellationToken));
        Assert.Contains("HTTP 503", rejected.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingSidecarIsReportedByTheRowReaderAndByThePipelineStatus()
    {
        var options = new ReceiptRecognitionOptions
        {
            RowsParserModel = "parser",
            AppleSidecarPath = Path.Combine(Path.GetTempPath(), $"missing-sidecar-{Guid.NewGuid():N}")
        };
        var reader = new AppleReceiptGeometryReader(
            Options.Create(options),
            NullLogger<AppleReceiptGeometryReader>.Instance);

        var error = await Assert.ThrowsAsync<ReceiptExtractionException>(
            () => reader.ReadAsync(
                new ReceiptImageData(new byte[] { 1, 2, 3 }, "image/jpeg"),
                TestContext.Current.CancellationToken));
        Assert.Contains("build-apple-sidecar.sh", error.Message, StringComparison.Ordinal);

        using var handler = new TextHandler { InstalledModels = ["parser"] };
        using var provider = CreateProvider(handler, options);
        var resolver = new StructuredTextProviderResolver([provider]);
        var pipeline = new AppleGeometryOllamaExtractor(
            reader,
            new ReceiptRowsAnalysisWorkflow(resolver),
            resolver,
            NullLogger<AppleGeometryOllamaExtractor>.Instance);

        Assert.Equal("apple-ollama", pipeline.Id);
        var status = await pipeline.GetStatusAsync(TestContext.Current.CancellationToken);
        Assert.False(status.Available);
        Assert.NotNull(status.Message);
    }

    [Fact]
    public void PipelineRequiresItsParserModelInConfiguration()
    {
        var descriptors = new[]
        {
            new ReceiptRecognitionPipelineDescriptor("ollama"),
            new ReceiptRecognitionPipelineDescriptor(AppleGeometryOllamaExtractor.PipelineId)
        };
        var options = new ReceiptRecognitionOptions
        {
            Pipeline = AppleGeometryOllamaExtractor.PipelineId,
            FallbackPipelines = ["ollama"],
            RowsParserModel = " "
        };

        var result = new ReceiptRecognitionOptionsValidator(descriptors).Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("RowsParserModel must not be empty.", result.Failures!);

        var defaults = new ReceiptRecognitionOptions();
        Assert.Equal("qwen3.5:35b", defaults.RowsParserModel);
        // 16384 and 1800 are the values research/ocr-geometry/drafts/parse.py used.
        Assert.Equal(16_384, defaults.RowsParserMaximumOutputTokens);
        Assert.Equal(1_800, defaults.RowsParserRequestTimeoutSeconds);

        var tooLargeBudget = new ReceiptRecognitionOptions
        {
            Pipeline = AppleGeometryOllamaExtractor.PipelineId,
            FallbackPipelines = [],
            ContextLength = 16_384,
            RowsParserMaximumOutputTokens = 16_384,
            RowsParserRequestTimeoutSeconds = 1_801
        };
        var budgetResult = new ReceiptRecognitionOptionsValidator(descriptors)
            .Validate(null, tooLargeBudget);
        Assert.True(budgetResult.Failed);
        Assert.Contains(
            budgetResult.Failures!,
            failure => failure.Contains(
                "RowsParserMaximumOutputTokens must be less than ContextLength",
                StringComparison.Ordinal));
        Assert.Contains(
            budgetResult.Failures!,
            failure => failure.Contains(
                "RowsParserRequestTimeoutSeconds must be between 1 and 1800",
                StringComparison.Ordinal));
    }

    private static StructuredTextRequest CreateRequest() => new(
        ReceiptRowsAnalysisContract.BuildPrompt([Page]),
        ReceiptRowsAnalysisContract.JsonSchema,
        ReceiptRowsAnalysisContract.PromptVersion,
        ReceiptRowsAnalysisContract.SchemaVersion,
        "correlation");

    private static OllamaStructuredTextProvider CreateProvider(
        HttpMessageHandler handler,
        ReceiptRecognitionOptions options) =>
        new(
            new HttpClient(handler, disposeHandler: false)
            {
                BaseAddress = new Uri("http://ollama.test/", UriKind.Absolute)
            },
            Options.Create(options),
            NullLogger<OllamaStructuredTextProvider>.Instance);

    private sealed class TextHandler : HttpMessageHandler
    {
        private readonly List<(string Path, string Body)> _requests = [];

        public IReadOnlyList<string> InstalledModels { get; init; } = ["parser"];

        public string Answer { get; init; } = ValidAnswer;

        public string DoneReason { get; init; } = "stop";

        public HttpStatusCode ChatStatusCode { get; init; } = HttpStatusCode.OK;

        public IReadOnlyList<string> RequestPaths =>
            _requests.Select(request => request.Path).ToArray();

        public JsonElement[] Bodies(string path) =>
            _requests
                .Where(request => request.Path == path)
                .Select(request => JsonDocument.Parse(request.Body).RootElement.Clone())
                .ToArray();

        public JsonElement SingleBody(string path) => Bodies(path).Single();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            _requests.Add((path, body));

            if (path == "/api/tags")
            {
                var models = string.Join(
                    ',',
                    InstalledModels.Select(model => $$"""{"name":{{JsonSerializer.Serialize(model)}}}"""));
                return Json(HttpStatusCode.OK, $$"""{"models":[{{models}}]}""");
            }

            if (path == "/api/chat")
            {
                return ChatStatusCode != HttpStatusCode.OK
                    ? new HttpResponseMessage(ChatStatusCode)
                    : Json(HttpStatusCode.OK, $$"""
                        {"message":{"content":{{JsonSerializer.Serialize(Answer)}}},
                         "done_reason":{{JsonSerializer.Serialize(DoneReason)}}}
                        """);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(HttpStatusCode statusCode, string payload) =>
            new(statusCode)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        private readonly Exception _exception;

        public ThrowingHandler(Exception exception) => _exception = exception;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromException<HttpResponseMessage>(_exception);
    }
}
