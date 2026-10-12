using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PurchaseMemory.Application.Receipts;
using PurchaseMemory.Infrastructure.Recognition;

namespace PurchaseMemory.IntegrationTests;

/// <summary>
/// The <c>anthropic</c> pipeline without network: request building and response
/// handling through the real SDK over a fake HTTP handler, error mapping, and
/// the router's fallback to OpenAI.
/// </summary>
public sealed class AnthropicStructuredVisionProviderTests
{
    private const string ValidReceiptJson = """
        {"rawLines":["CHLEB 4,99"],"merchantName":"BIEDRONKA","purchasedAt":null,"currency":"PLN","receiptTotal":4.99,"receiptTotalBoundingBox":null,"items":[{"name":"CHLEB","quantity":1,"unit":null,"unitPrice":4.99,"lineTotal":4.99,"boundingBox":null}]}
        """;

    private static readonly string[] SchemaOnlyKeywords =
    [
        "minimum", "maximum", "exclusiveMinimum", "exclusiveMaximum", "minLength", "maxLength",
        "pattern", "minItems", "maxItems", "$schema", "$id", "title"
    ];

    [Fact]
    public async Task SdkRequestCarriesPromptImagesAdaptedSchemaEffortAndLimits()
    {
        using var handler = new FakeAnthropicHandler(_ => Json(HttpStatusCode.OK, MessageBody("end_turn", ValidReceiptJson)));
        using var transport = new AnthropicMessagesTransport("test-only-key", TimeSpan.FromSeconds(5), 0, handler);
        byte[] jpeg = [0xff, 0xd8, 0xff];
        byte[] png = [1, 2, 3];

        var result = await transport.CreateAsync(
            new AnthropicMessagesRequest(
                [new ReceiptImageData(jpeg, "image/jpeg"), new ReceiptImageData(png, "image/png")],
                "shared prompt marker",
                ReceiptAnalysisContract.JsonSchema,
                "claude-haiku-5-5",
                AnthropicEffort.Low,
                1_234),
            TestContext.Current.CancellationToken);

        Assert.Equal(ValidReceiptJson, result.Json);
        Assert.Equal("end_turn", result.StopReason);
        Assert.Equal("msg_test_1", result.MessageId);
        Assert.Equal(2_844, result.InputTokens);
        Assert.Equal(1_900, result.OutputTokens);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("/v1/messages", request.Path);
        Assert.Equal("test-only-key", request.ApiKeyHeader);
        Assert.DoesNotContain("test-only-key", request.Body, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(request.Body);
        var root = document.RootElement;
        Assert.Equal("claude-haiku-5-5", root.GetProperty("model").GetString());
        Assert.Equal(1_234, root.GetProperty("max_tokens").GetInt32());
        Assert.Equal("adaptive", root.GetProperty("thinking").GetProperty("type").GetString());
        var outputConfig = root.GetProperty("output_config");
        Assert.Equal("low", outputConfig.GetProperty("effort").GetString());
        Assert.Equal("json_schema", outputConfig.GetProperty("format").GetProperty("type").GetString());
        Assert.True(JsonElement.DeepEquals(
            JsonDocument.Parse(AnthropicJsonSchemaAdapter.AdaptToJson(ReceiptAnalysisContract.JsonSchema)).RootElement,
            outputConfig.GetProperty("format").GetProperty("schema")));

        var message = Assert.Single(root.GetProperty("messages").EnumerateArray());
        Assert.Equal("user", message.GetProperty("role").GetString());
        var content = message.GetProperty("content");
        Assert.Equal(3, content.GetArrayLength());
        Assert.Equal("text", content[0].GetProperty("type").GetString());
        Assert.Equal("shared prompt marker", content[0].GetProperty("text").GetString());
        AssertImage(content[1], "image/jpeg", jpeg);
        AssertImage(content[2], "image/png", png);
    }

    [Fact]
    public void SchemaAdapterRemovesUnsupportedKeywordsAndKeepsTheShape()
    {
        var adapted = AnthropicJsonSchemaAdapter.AdaptToJson(ReceiptAnalysisContract.JsonSchema);

        foreach (var keyword in SchemaOnlyKeywords)
        {
            Assert.DoesNotContain($"\"{keyword}\"", adapted, StringComparison.Ordinal);
        }

        using var original = JsonDocument.Parse(ReceiptAnalysisContract.JsonSchema);
        using var stripped = JsonDocument.Parse(adapted);
        var root = stripped.RootElement;
        Assert.Equal("object", root.GetProperty("type").GetString());
        Assert.False(root.GetProperty("additionalProperties").GetBoolean());
        Assert.True(JsonElement.DeepEquals(original.RootElement.GetProperty("required"), root.GetProperty("required")));
        var item = root.GetProperty("properties").GetProperty("items").GetProperty("items");
        Assert.True(JsonElement.DeepEquals(
            original.RootElement.GetProperty("properties").GetProperty("items").GetProperty("items").GetProperty("required"),
            item.GetProperty("required")));
        Assert.Equal(
            ["string", "null"],
            item.GetProperty("properties").GetProperty("unit").GetProperty("type").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal(
            "Negative for a discount row (OPUST/RABAT).",
            item.GetProperty("properties").GetProperty("lineTotal").GetProperty("description").GetString());
    }

    [Fact]
    public void SchemaAdapterKeepsAPropertyNamedLikeAKeyword()
    {
        var adapted = AnthropicJsonSchemaAdapter.Adapt("""
            {"type":"object","properties":{"pattern":{"type":"string","pattern":"^a$"},"title":{"type":"string"}},"required":["pattern","title"]}
            """);

        var properties = adapted["properties"];
        Assert.True(properties.TryGetProperty("pattern", out var pattern));
        Assert.False(pattern.TryGetProperty("pattern", out _));
        Assert.True(properties.TryGetProperty("title", out _));
    }

    [Fact]
    public async Task SdkResponseWithRefusalReportsStopReasonAndCategory()
    {
        using var handler = new FakeAnthropicHandler(_ => Json(HttpStatusCode.OK, MessageBody("refusal", text: null, refusalCategory: "cyber")));
        using var transport = new AnthropicMessagesTransport("test-only-key", TimeSpan.FromSeconds(5), 0, handler);

        var result = await transport.CreateAsync(CreateTransportRequest(), TestContext.Current.CancellationToken);

        Assert.Equal("refusal", result.StopReason);
        Assert.Equal("cyber", result.RefusalCategory);
        Assert.Null(result.Json);
    }

    [Theory]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(529)]
    [InlineData(400)]
    public async Task SdkHttpErrorsBecomeServiceExceptionsWithTheStatus(int status)
    {
        using var handler = new FakeAnthropicHandler(_ => Json(
            (HttpStatusCode)status,
            """{"type":"error","error":{"type":"overloaded_error","message":"upstream-secret-payload"}}"""));
        using var transport = new AnthropicMessagesTransport("test-only-key", TimeSpan.FromSeconds(5), 0, handler);

        var exception = await Assert.ThrowsAsync<AnthropicProviderException>(
            () => transport.CreateAsync(CreateTransportRequest(), TestContext.Current.CancellationToken));

        Assert.Equal(status, exception.StatusCode);
        Assert.DoesNotContain("upstream-secret-payload", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SdkTimeoutIsReportedAsATimeoutByTheProvider()
    {
        using var handler = new FakeAnthropicHandler(async cancellationToken =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return Json(HttpStatusCode.OK, MessageBody("end_turn", ValidReceiptJson));
        });
        using var transport = new AnthropicMessagesTransport("test-only-key", TimeSpan.FromMilliseconds(200), 0, handler);
        using var provider = CreateProvider(transport);

        var exception = await Assert.ThrowsAsync<ReceiptExtractionException>(
            () => provider.GenerateAsync(CreateRequest(), TestContext.Current.CancellationToken));

        Assert.Contains("timed out", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnsupportedImageTypeFailsRecoverably()
    {
        using var handler = new FakeAnthropicHandler(_ => Json(HttpStatusCode.OK, MessageBody("end_turn", ValidReceiptJson)));
        using var transport = new AnthropicMessagesTransport("test-only-key", TimeSpan.FromSeconds(5), 0, handler);

        await Assert.ThrowsAsync<ReceiptExtractionException>(() => transport.CreateAsync(
            CreateTransportRequest() with { Images = [new ReceiptImageData(new byte[] { 1 }, "image/heic")] },
            TestContext.Current.CancellationToken));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task OversizedPhotoIsRefusedBeforeAnyUpload()
    {
        using var handler = new FakeAnthropicHandler(_ => Json(HttpStatusCode.OK, MessageBody("end_turn", ValidReceiptJson)));
        using var transport = new AnthropicMessagesTransport("test-only-key", TimeSpan.FromSeconds(5), 0, handler);
        var tooLarge = new byte[(int)(AnthropicMessagesTransport.MaximumBase64BytesPerImage / 4 * 3) + 3];

        var exception = await Assert.ThrowsAsync<ReceiptExtractionException>(() => transport.CreateAsync(
            CreateTransportRequest() with { Images = [new ReceiptImageData(tooLarge, "image/jpeg")] },
            TestContext.Current.CancellationToken));

        Assert.Contains("too large", exception.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void PhotoJustUnderThePerImageLimitIsAccepted()
    {
        var fits = new byte[(int)(AnthropicMessagesTransport.MaximumBase64BytesPerImage / 4 * 3)];

        AnthropicMessagesTransport.EnsureWithinSizeLimits([new ReceiptImageData(fits, "image/jpeg")]);
    }

    [Fact]
    public async Task ProviderReturnsJsonAndUsage()
    {
        var transport = new RecordingTransport(Completed());
        using var provider = CreateProvider(transport);

        var response = await provider.GenerateAsync(CreateRequest(), TestContext.Current.CancellationToken);

        Assert.Equal(ValidReceiptJson, response.RawJson);
        Assert.Equal("anthropic", response.Provider);
        Assert.Equal("claude-haiku-5-5", response.Model);
        Assert.Equal("msg_test_1", response.ProviderRequestId);
        Assert.Equal("end_turn", response.FinishReason);
        Assert.Equal(2_844, response.InputTokens);
        Assert.Equal(1_900, response.OutputTokens);
        var sent = Assert.Single(transport.Requests);
        Assert.Equal("shared prompt", sent.Prompt);
        Assert.Equal(ReceiptAnalysisContract.JsonSchema, sent.JsonSchema);
        Assert.Equal(AnthropicEffort.Low, sent.Effort);
        Assert.Equal(16_384, sent.MaximumOutputTokens);
    }

    [Theory]
    [InlineData("refusal", "declined")]
    [InlineData("max_tokens", "incomplete")]
    [InlineData("pause_turn", "did not complete")]
    [InlineData(null, "did not complete")]
    public async Task NonNormalStopReasonsFailRecoverably(string? stopReason, string expected)
    {
        var transport = new RecordingTransport(Completed() with { StopReason = stopReason, RefusalCategory = "cyber" });
        using var provider = CreateProvider(transport);

        var exception = await Assert.ThrowsAsync<ReceiptExtractionException>(
            () => provider.GenerateAsync(CreateRequest(), TestContext.Current.CancellationToken));

        Assert.Contains(expected, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EmptyTextFailsRecoverably()
    {
        var transport = new RecordingTransport(Completed() with { Json = " " });
        using var provider = CreateProvider(transport);

        var exception = await Assert.ThrowsAsync<ReceiptExtractionException>(
            () => provider.GenerateAsync(CreateRequest(), TestContext.Current.CancellationToken));

        Assert.Contains("empty", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(401, "credential")]
    [InlineData(429, "rate limit")]
    [InlineData(529, "overloaded")]
    [InlineData(503, "temporarily unavailable")]
    [InlineData(400, "rejected")]
    public async Task MapsHttpErrorsWithoutLeakingServicePayload(int status, string expected)
    {
        const string sensitivePayload = "upstream-secret-payload";
        var transport = new ThrowingTransport(new AnthropicProviderException(
            status, sensitivePayload, new InvalidOperationException(sensitivePayload)));
        using var provider = CreateProvider(transport);

        var exception = await Assert.ThrowsAsync<ReceiptExtractionException>(
            () => provider.GenerateAsync(CreateRequest(), TestContext.Current.CancellationToken));

        Assert.Contains(expected, exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(sensitivePayload, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MapsNetworkFailure()
    {
        var transport = new ThrowingTransport(new AnthropicProviderException(
            0, "transport failure", new HttpRequestException("DNS failed")));
        using var provider = CreateProvider(transport);

        var exception = await Assert.ThrowsAsync<ReceiptExtractionException>(
            () => provider.GenerateAsync(CreateRequest(), TestContext.Current.CancellationToken));

        Assert.Contains("could not be reached", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CallerCancellationIsPropagated()
    {
        var transport = new ThrowingTransport(new OperationCanceledException());
        using var provider = CreateProvider(transport);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.GenerateAsync(CreateRequest(), cancellation.Token));
    }

    [Fact]
    public async Task MissingKeyFailsRecoverablyAndStatusExplainsIt()
    {
        using var provider = new AnthropicStructuredVisionProvider(
            Options.Create(new ReceiptRecognitionOptions { AnthropicModel = "claude-haiku-5-5" }),
            NullLogger<AnthropicStructuredVisionProvider>.Instance,
            testTransport: null,
            readApiKey: () => null);

        var exception = await Assert.ThrowsAsync<ReceiptExtractionException>(
            () => provider.GenerateAsync(CreateRequest(), TestContext.Current.CancellationToken));
        var status = await provider.GetStatusAsync(TestContext.Current.CancellationToken);

        Assert.Contains("ANTHROPIC_API_KEY", exception.Message, StringComparison.Ordinal);
        Assert.False(status.Available);
        Assert.Contains("ANTHROPIC_API_KEY", status.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RouterUsesAnthropicFirstWhenItSucceeds()
    {
        var transport = new RecordingTransport(Completed());
        using var provider = CreateProvider(transport);
        var openAi = new FakeOpenAiPipeline();
        var router = CreateRouter(provider, openAi);

        var extraction = await router.ExtractAsync(Images(), TestContext.Current.CancellationToken);

        Assert.Equal("anthropic", extraction.ExtractorName);
        Assert.Equal("claude-haiku-5-5", extraction.ModelName);
        Assert.Equal(0, openAi.Calls);
    }

    [Fact]
    public async Task RouterFallsBackToOpenAiOnRefusal()
    {
        var transport = new RecordingTransport(Completed() with { StopReason = "refusal", Json = null });
        using var provider = CreateProvider(transport);
        var openAi = new FakeOpenAiPipeline();

        var extraction = await CreateRouter(provider, openAi).ExtractAsync(Images(), TestContext.Current.CancellationToken);

        Assert.Equal("openai", extraction.ExtractorName);
        Assert.Equal(1, openAi.Calls);
        Assert.Single(transport.Requests);
    }

    [Fact]
    public async Task RouterFallsBackToOpenAiWhenJsonStaysInvalidAfterRepair()
    {
        var transport = new RecordingTransport(Completed() with { Json = "{not-json" });
        using var provider = CreateProvider(transport);
        var openAi = new FakeOpenAiPipeline();

        var extraction = await CreateRouter(provider, openAi).ExtractAsync(Images(), TestContext.Current.CancellationToken);

        Assert.Equal("openai", extraction.ExtractorName);
        Assert.Equal(2, transport.Requests.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(529)]
    public async Task RouterFallsBackToOpenAiOnTimeoutOrServiceErrors(int status)
    {
        Exception inner = status == 0 ? new TaskCanceledException("timeout") : new InvalidOperationException();
        var transport = new ThrowingTransport(new AnthropicProviderException(status, "failed", inner));
        using var provider = CreateProvider(transport);
        var openAi = new FakeOpenAiPipeline();

        var extraction = await CreateRouter(provider, openAi).ExtractAsync(Images(), TestContext.Current.CancellationToken);

        Assert.Equal("openai", extraction.ExtractorName);
    }

    [Fact]
    public async Task WithoutAnthropicKeyTheRouterUsesOpenAiAndStatusReportsTheFallback()
    {
        using var provider = new AnthropicStructuredVisionProvider(
            Options.Create(new ReceiptRecognitionOptions { AnthropicModel = "claude-haiku-5-5" }),
            NullLogger<AnthropicStructuredVisionProvider>.Instance,
            testTransport: null,
            readApiKey: () => null);
        var openAi = new FakeOpenAiPipeline();
        var router = CreateRouter(provider, openAi);

        var extraction = await router.ExtractAsync(Images(), TestContext.Current.CancellationToken);
        var status = await router.GetStatusAsync(TestContext.Current.CancellationToken);

        Assert.Equal("openai", extraction.ExtractorName);
        Assert.True(status.Available);
        Assert.Equal("openai fallback", status.Mode);
        Assert.Contains("ANTHROPIC_API_KEY", status.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionSettingsBindToAnthropicFirstWithOpenAiFallbackAndValidate()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(FindAppSettings(), optional: false)
            .Build();
        var options = new ReceiptRecognitionOptions();
        configuration.GetSection(ReceiptRecognitionOptions.SectionName).Bind(options);

        Assert.Equal("anthropic", options.Pipeline);
        Assert.Equal(["openai", "ollama"], options.FallbackPipelines);
        Assert.Equal("claude-haiku-5-5", options.AnthropicModel);
        Assert.Equal(AnthropicEffort.Low, options.AnthropicEffort);
        Assert.Equal(120, options.AnthropicRequestTimeoutSeconds);
        Assert.Equal(16_384, options.AnthropicMaximumOutputTokens);
        Assert.Equal(1, options.AnthropicMaximumRetries);
        Assert.Equal("gpt-6-sol", options.OpenAiModel);

        var validator = new ReceiptRecognitionOptionsValidator(
        [
            new ReceiptRecognitionPipelineDescriptor("apple"),
            new ReceiptRecognitionPipelineDescriptor("ollama"),
            new ReceiptRecognitionPipelineDescriptor("openai"),
            new ReceiptRecognitionPipelineDescriptor("anthropic")
        ]);
        Assert.True(validator.Validate(null, options).Succeeded);
    }

    private static string FindAppSettings()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "PurchaseMemory.Api", "appsettings.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("src/PurchaseMemory.Api/appsettings.json was not found.");
    }

    private static ReceiptExtractorRouter CreateRouter(
        AnthropicStructuredVisionProvider provider,
        IReceiptRecognitionPipeline openAi)
    {
        var resolver = new StructuredVisionProviderResolver([provider]);
        var anthropic = new AnthropicReceiptExtractor(
            new ReceiptAnalysisWorkflow(resolver),
            resolver,
            NullLogger<AnthropicReceiptExtractor>.Instance);
        return new ReceiptExtractorRouter(
            [anthropic, openAi],
            Options.Create(new ReceiptRecognitionOptions
            {
                Pipeline = "anthropic",
                FallbackPipelines = ["openai"],
                AnthropicModel = "claude-haiku-5-5"
            }),
            NullLogger<ReceiptExtractorRouter>.Instance);
    }

    private static AnthropicStructuredVisionProvider CreateProvider(IAnthropicMessagesTransport transport) =>
        new(
            Options.Create(new ReceiptRecognitionOptions { AnthropicModel = "claude-haiku-5-5" }),
            NullLogger<AnthropicStructuredVisionProvider>.Instance,
            transport,
            () => "test-key");

    private static ReceiptImageData[] Images() => [new ReceiptImageData(new byte[] { 0xff, 0xd8, 0xff }, "image/jpeg")];

    private static StructuredVisionRequest CreateRequest() => new(
        Images(),
        "shared prompt",
        ReceiptAnalysisContract.JsonSchema,
        ReceiptAnalysisContract.PromptVersion,
        ReceiptAnalysisContract.SchemaVersion,
        "correlation-1");

    private static AnthropicMessagesRequest CreateTransportRequest() => new(
        Images(),
        "shared prompt",
        ReceiptAnalysisContract.JsonSchema,
        "claude-haiku-5-5",
        AnthropicEffort.Low,
        1_000);

    private static AnthropicMessagesResult Completed() => new(
        ValidReceiptJson, "end_turn", null, "msg_test_1", 2_844, 1_900);

    private static void AssertImage(JsonElement block, string mediaType, byte[] bytes)
    {
        Assert.Equal("image", block.GetProperty("type").GetString());
        var source = block.GetProperty("source");
        Assert.Equal("base64", source.GetProperty("type").GetString());
        Assert.Equal(mediaType, source.GetProperty("media_type").GetString());
        Assert.Equal(Convert.ToBase64String(bytes), source.GetProperty("data").GetString());
    }

    private static string MessageBody(string stopReason, string? text, string? refusalCategory = null)
    {
        var content = text is null
            ? "[]"
            : $$"""[{"type":"thinking","thinking":"","signature":"sig"},{"type":"text","text":{{JsonSerializer.Serialize(text)}}}]""";
        var stopDetails = refusalCategory is null
            ? "null"
            : $$"""{"type":"refusal","category":"{{refusalCategory}}","explanation":"declined"}""";
        return $$$"""
            {"id":"msg_test_1","type":"message","role":"assistant","model":"claude-haiku-5-5","content":{{{content}}},"stop_reason":"{{{stopReason}}}","stop_sequence":null,"stop_details":{{{stopDetails}}},"usage":{"input_tokens":2844,"output_tokens":1900,"cache_creation_input_tokens":0,"cache_read_input_tokens":0}}
            """;
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed record CapturedRequest(string Path, string? ApiKeyHeader, string Body);

    private sealed class FakeAnthropicHandler : HttpMessageHandler
    {
        private readonly Func<CancellationToken, Task<HttpResponseMessage>> _respond;

        public FakeAnthropicHandler(Func<CancellationToken, HttpResponseMessage> respond)
            : this(cancellationToken => Task.FromResult(respond(cancellationToken)))
        {
        }

        public FakeAnthropicHandler(Func<CancellationToken, Task<HttpResponseMessage>> respond)
        {
            _respond = respond;
        }

        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new CapturedRequest(
                request.RequestUri!.AbsolutePath,
                request.Headers.TryGetValues("x-api-key", out var values) ? values.Single() : null,
                body));
            return await _respond(cancellationToken);
        }
    }

    private sealed class RecordingTransport(AnthropicMessagesResult response) : IAnthropicMessagesTransport
    {
        public List<AnthropicMessagesRequest> Requests { get; } = [];

        public Task<AnthropicMessagesResult> CreateAsync(
            AnthropicMessagesRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            return Task.FromResult(response);
        }
    }

    private sealed class ThrowingTransport(Exception exception) : IAnthropicMessagesTransport
    {
        public Task<AnthropicMessagesResult> CreateAsync(
            AnthropicMessagesRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromException<AnthropicMessagesResult>(exception);
        }
    }

    private sealed class FakeOpenAiPipeline : IReceiptRecognitionPipeline
    {
        public int Calls { get; private set; }

        public string Id => "openai";

        public Task<ReceiptExtractionResult> ExtractAsync(
            IReadOnlyList<ReceiptImageData> images,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new ReceiptExtractionResult(
                "LIDL", null, "PLN", 1m, [new ReceiptExtractionItem("X", 1m, null, 1m, 1m)], "openai", "gpt-6-sol"));
        }

        public Task<ReceiptRecognitionStatus> GetStatusAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ReceiptRecognitionStatus(true, "OpenAI", ["gpt-6-sol"], null));
    }
}
