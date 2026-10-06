using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PurchaseMemory.Application.Receipts;
using PurchaseMemory.Infrastructure.Recognition;

namespace PurchaseMemory.IntegrationTests;

public sealed class OpenAiStructuredVisionProviderTests
{
    private const string ValidReceiptJson = """
        {
          "merchantName": "BIEDRONKA",
          "purchasedAt": null,
          "currency": null,
          "receiptTotal": null,
          "items": []
        }
        """;

    [Fact]
    public async Task OfficialSdkForwardsImagePromptStrictSchemaDetailModelAndLimits()
    {
        using var handler = new InspectingOpenAiHandler();
        using var transport = new OpenAiResponsesTransport(
            "test-only-key",
            "test-vision-model",
            TimeSpan.FromSeconds(5),
            handler);
        var request = new OpenAiResponsesRequest(
            [new ReceiptImageData(new byte[] { 0xff, 0xd8, 0xff }, "image/jpeg"), new ReceiptImageData(new byte[] { 1, 2, 3 }, "image/png")],
            "shared prompt marker",
            ReceiptAnalysisContract.JsonSchema,
            "test-vision-model",
            OpenAiImageDetail.Original,
            OpenAiReasoningEffort.Low,
            1_234);

        var result = await transport.CreateAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(ValidReceiptJson, result.Json);
        Assert.Equal("Completed", result.Status);
        Assert.Equal("openai-request-1", result.RequestId);
        Assert.Equal(31, result.InputTokens);
        Assert.Equal(17, result.OutputTokens);
        Assert.NotNull(handler.RequestJson);
        var root = handler.RequestJson.RootElement;
        Assert.Equal("test-vision-model", root.GetProperty("model").GetString());
        Assert.Equal(1_234, root.GetProperty("max_output_tokens").GetInt32());
        Assert.False(root.GetProperty("store").GetBoolean());
        Assert.Equal("low", root.GetProperty("reasoning").GetProperty("effort").GetString());

        var content = root.GetProperty("input")[0].GetProperty("content");
        Assert.Equal("input_text", content[0].GetProperty("type").GetString());
        Assert.Equal("shared prompt marker", content[0].GetProperty("text").GetString());
        Assert.Equal("input_image", content[1].GetProperty("type").GetString());
        Assert.Equal("original", content[1].GetProperty("detail").GetString());
        Assert.Equal(
            $"data:image/jpeg;base64,{Convert.ToBase64String(request.Images[0].Content.Span)}",
            content[1].GetProperty("image_url").GetString());
        Assert.Equal("input_image", content[2].GetProperty("type").GetString());
        Assert.Equal($"data:image/png;base64,{Convert.ToBase64String(request.Images[1].Content.Span)}", content[2].GetProperty("image_url").GetString());

        var format = root.GetProperty("text").GetProperty("format");
        Assert.Equal("json_schema", format.GetProperty("type").GetString());
        Assert.Equal("purchase_memory_receipt_extract_v1", format.GetProperty("name").GetString());
        Assert.True(format.GetProperty("strict").GetBoolean());
        Assert.True(JsonElement.DeepEquals(
            JsonDocument.Parse(ReceiptAnalysisContract.JsonSchema).RootElement,
            format.GetProperty("schema")));
        Assert.DoesNotContain("test-only-key", handler.RawRequestBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StatusDoesNotCallModelAndExplainsMissingKey()
    {
        var transport = new RecordingTransport(CompletedResponse());
        using var provider = CreateProvider(transport, () => null);

        var status = await provider.GetStatusAsync(TestContext.Current.CancellationToken);

        Assert.False(status.Available);
        Assert.Contains("OPENAI_API_KEY", status.Message, StringComparison.Ordinal);
        Assert.Equal(0, transport.CallCount);
    }

    [Fact]
    public async Task RecognitionWithoutKeyFailsWithARecoverableErrorSoTheRouterCanFallBack()
    {
        // The production path (no test transport): a missing key must surface as
        // ReceiptExtractionException, the one type the router falls back on, and
        // never as a startup failure or an unhandled exception.
        using var provider = new OpenAiStructuredVisionProvider(
            Options.Create(new ReceiptRecognitionOptions { OpenAiModel = "gpt-6-sol" }),
            NullLogger<OpenAiStructuredVisionProvider>.Instance,
            testTransport: null,
            readApiKey: () => null);

        var exception = await Assert.ThrowsAsync<ReceiptExtractionException>(
            () => provider.GenerateAsync(CreateRequest(), TestContext.Current.CancellationToken));

        Assert.Contains("OPENAI_API_KEY", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StatusExplainsMissingModelBeforeCheckingKey()
    {
        var transport = new RecordingTransport(CompletedResponse());
        using var provider = CreateProvider(
            transport,
            () => "test-key",
            options => options.OpenAiModel = " ");

        var status = await provider.GetStatusAsync(TestContext.Current.CancellationToken);

        Assert.False(status.Available);
        Assert.Contains("model", status.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, transport.CallCount);
    }

    [Fact]
    public async Task CallerCancellationIsPropagated()
    {
        var transport = new ThrowingTransport(
            cancellationToken => Task.FromCanceled<OpenAiResponsesResult>(cancellationToken));
        using var provider = CreateProvider(transport, () => "test-key");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.GenerateAsync(CreateRequest(), cancellation.Token));
    }

    [Theory]
    [InlineData(401, "credential")]
    [InlineData(429, "rate limit")]
    [InlineData(500, "rejected")]
    public async Task MapsHttpErrorsWithoutLeakingServicePayload(int status, string expected)
    {
        const string sensitivePayload = "upstream-secret-payload";
        var transport = new ThrowingTransport(_ => Task.FromException<OpenAiResponsesResult>(
            new OpenAiServiceException(
                status,
                sensitivePayload,
                new InvalidOperationException(sensitivePayload))));
        using var provider = CreateProvider(transport, () => "test-key");

        var exception = await Assert.ThrowsAsync<ReceiptExtractionException>(
            () => provider.GenerateAsync(CreateRequest(), TestContext.Current.CancellationToken));

        Assert.Contains(expected, exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(sensitivePayload, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MapsWrappedTimeoutWithStatusZero()
    {
        var transport = new ThrowingTransport(_ => Task.FromException<OpenAiResponsesResult>(
            new OpenAiServiceException(
                0,
                "transport failure",
                new TaskCanceledException("HTTP timeout"))));
        using var provider = CreateProvider(transport, () => "test-key");

        var exception = await Assert.ThrowsAsync<ReceiptExtractionException>(
            () => provider.GenerateAsync(CreateRequest(), TestContext.Current.CancellationToken));

        Assert.Contains("timed out", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MapsWrappedNetworkFailureWithStatusZero()
    {
        var transport = new ThrowingTransport(_ => Task.FromException<OpenAiResponsesResult>(
            new OpenAiServiceException(
                0,
                "transport failure",
                new HttpRequestException("DNS failed"))));
        using var provider = CreateProvider(transport, () => "test-key");

        var exception = await Assert.ThrowsAsync<ReceiptExtractionException>(
            () => provider.GenerateAsync(CreateRequest(), TestContext.Current.CancellationToken));

        Assert.Contains("network", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("refusal", "completed", null, "not allowed", "refused")]
    [InlineData("incomplete", "incomplete", "max_output_tokens", null, "incomplete")]
    [InlineData("empty", "completed", null, null, "empty")]
    [InlineData("failed", "failed", null, null, "did not complete")]
    public async Task MapsNonSuccessfulResponses(
        string _,
        string status,
        string? incompleteReason,
        string? refusal,
        string expected)
    {
        var transport = new RecordingTransport(new OpenAiResponsesResult(
            refusal is null && status == "completed" ? " " : ValidReceiptJson,
            refusal,
            "request-id",
            status,
            incompleteReason,
            1,
            1));
        using var provider = CreateProvider(transport, () => "test-key");

        var exception = await Assert.ThrowsAsync<ReceiptExtractionException>(
            () => provider.GenerateAsync(CreateRequest(), TestContext.Current.CancellationToken));

        Assert.Contains(expected, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvalidJsonIsRejectedBySharedWorkflowAfterBoundedRepair()
    {
        var transport = new RecordingTransport(CompletedResponse("{not-json"));
        using var provider = CreateProvider(transport, () => "test-key");
        var workflow = new ReceiptAnalysisWorkflow(
            new StructuredVisionProviderResolver([provider]));
        var request = CreateRequest();

        await Assert.ThrowsAsync<ReceiptStructuredOutputException>(
            () => workflow.AnalyzeAsync(
                new ReceiptAnalysisRequest(
                    Guid.NewGuid(),
                    request.CorrelationId,
                    request.Images,
                    provider.Id),
                TestContext.Current.CancellationToken));

        Assert.Equal(2, transport.CallCount);
    }

    [Fact]
    public async Task ExistingRouterSelectsOpenAiFacadeByConfiguration()
    {
        var transport = new RecordingTransport(CompletedResponse());
        using var provider = CreateProvider(transport, () => "test-key");
        var resolver = new StructuredVisionProviderResolver([provider]);
        var facade = new OpenAiReceiptExtractor(
            new ReceiptAnalysisWorkflow(resolver),
            resolver,
            NullLogger<OpenAiReceiptExtractor>.Instance);
        var router = new ReceiptExtractorRouter(
            [facade],
            Options.Create(new ReceiptRecognitionOptions
            {
                Pipeline = "openai",
                FallbackPipelines = [],
                OpenAiModel = "test-vision-model"
            }),
            NullLogger<ReceiptExtractorRouter>.Instance);
        var extraction = await router.ExtractAsync(
            [new ReceiptImageData(new byte[] { 0xff, 0xd8, 0xff }, "image/jpeg")],
            TestContext.Current.CancellationToken);

        Assert.Empty(extraction.Items);
        Assert.Equal(1, transport.CallCount);
    }

    private static OpenAiStructuredVisionProvider CreateProvider(
        IOpenAiResponsesTransport transport,
        Func<string?> readApiKey,
        Action<ReceiptRecognitionOptions>? configure = null)
    {
        var options = new ReceiptRecognitionOptions
        {
            OpenAiModel = "test-vision-model",
            OpenAiImageDetail = OpenAiImageDetail.High,
            OpenAiReasoningEffort = OpenAiReasoningEffort.Low,
            OpenAiMaximumOutputTokens = 1_234,
            OpenAiRequestTimeoutSeconds = 5
        };
        configure?.Invoke(options);
        return new OpenAiStructuredVisionProvider(
            Options.Create(options),
            NullLogger<OpenAiStructuredVisionProvider>.Instance,
            transport,
            readApiKey);
    }

    private static StructuredVisionRequest CreateRequest() => new(
        [new ReceiptImageData(new byte[] { 0xff, 0xd8, 0xff }, "image/jpeg")],
        "shared prompt",
        ReceiptAnalysisContract.JsonSchema,
        ReceiptAnalysisContract.PromptVersion,
        ReceiptAnalysisContract.SchemaVersion,
        "correlation-1");

    private static OpenAiResponsesResult CompletedResponse(string json = ValidReceiptJson) => new(
        json,
        null,
        "request-id",
        "completed",
        null,
        31,
        17);

    private sealed class RecordingTransport(OpenAiResponsesResult response)
        : IOpenAiResponsesTransport
    {
        public int CallCount { get; private set; }

        public Task<OpenAiResponsesResult> CreateAsync(
            OpenAiResponsesRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return Task.FromResult(response);
        }
    }

    private sealed class ThrowingTransport(
        Func<CancellationToken, Task<OpenAiResponsesResult>> implementation)
        : IOpenAiResponsesTransport
    {
        public Task<OpenAiResponsesResult> CreateAsync(
            OpenAiResponsesRequest request,
            CancellationToken cancellationToken) =>
            implementation(cancellationToken);
    }

    private sealed class InspectingOpenAiHandler : HttpMessageHandler
    {
        public JsonDocument? RequestJson { get; private set; }

        public string RawRequestBody { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/v1/responses", request.RequestUri?.AbsolutePath);
            Assert.Equal("application/json", request.Content?.Headers.ContentType?.MediaType);
            RawRequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            RequestJson = JsonDocument.Parse(RawRequestBody);
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    CreateApiResponse(),
                    Encoding.UTF8,
                    "application/json")
            };
            response.Headers.Add("x-request-id", "openai-request-1");
            return response;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                RequestJson?.Dispose();
            }

            base.Dispose(disposing);
        }

        private static string CreateApiResponse() => JsonSerializer.Serialize(new
        {
            id = "resp_test",
            @object = "response",
            created_at = 1_750_000_000,
            status = "completed",
            error = (object?)null,
            incomplete_details = (object?)null,
            instructions = (object?)null,
            max_output_tokens = 1234,
            model = "test-vision-model",
            output = new object[]
            {
                new
                {
                    id = "msg_test",
                    type = "message",
                    status = "completed",
                    role = "assistant",
                    content = new[]
                    {
                        new
                        {
                            type = "output_text",
                            annotations = Array.Empty<object>(),
                            logprobs = Array.Empty<object>(),
                            text = ValidReceiptJson
                        }
                    }
                }
            },
            parallel_tool_calls = true,
            previous_response_id = (object?)null,
            reasoning = new { effort = "low", summary = (object?)null },
            store = false,
            temperature = 1,
            text = new { format = new { type = "json_object" } },
            tool_choice = "auto",
            tools = Array.Empty<object>(),
            top_p = 1,
            truncation = "disabled",
            usage = new
            {
                input_tokens = 31,
                input_tokens_details = new { cached_tokens = 0 },
                output_tokens = 17,
                output_tokens_details = new { reasoning_tokens = 0 },
                total_tokens = 48
            },
            metadata = new { }
        });
    }
}
