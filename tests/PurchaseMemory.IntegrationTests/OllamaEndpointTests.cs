using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PurchaseMemory.Application.Receipts;
using PurchaseMemory.Infrastructure.Recognition;

namespace PurchaseMemory.IntegrationTests;

/// <summary>
/// Direct-to-Ollama (hosted or local) transport used by the receipt benchmark's
/// cloud configurations. Every test runs against a stub handler — no network.
/// </summary>
public sealed class OllamaEndpointTests
{
    // Obviously fake; the tests assert that this exact text never escapes into
    // messages or logs, only into the Authorization header.
    private const string FakeKey = "test-key-5f1c-DO-NOT-LEAK";

    [Fact]
    public async Task RemoteEndpointSendsBearerKeyOnEveryRequest()
    {
        var stub = new StubHandler(HttpStatusCode.OK, ChatBody);
        using var provider = CreateTextProvider(OllamaEndpoint.Create("https://ollama.com", FakeKey), stub, verify: true);

        await provider.GenerateAsync(TextRequest(), TestContext.Current.CancellationToken);

        Assert.Equal(["/api/tags", "/api/chat"], stub.Requests.Select(request => request.Path));
        Assert.All(stub.Requests, request => Assert.Equal($"Bearer {FakeKey}", request.Authorization));
        Assert.Equal("https://ollama.com/api/chat", stub.Requests[^1].Uri);
    }

    [Fact]
    public async Task LocalEndpointWithoutKeySendsNoAuthorizationHeader()
    {
        var stub = new StubHandler(HttpStatusCode.OK, ChatBody);
        var endpoint = OllamaEndpoint.Create("http://127.0.0.1:11434", apiKey: "  ");
        using var provider = CreateTextProvider(endpoint, stub, verify: true);

        await provider.GenerateAsync(TextRequest(), TestContext.Current.CancellationToken);

        Assert.False(endpoint.IsRemote);
        Assert.False(endpoint.HasApiKey);
        Assert.Equal("http://127.0.0.1:11434/api/chat", stub.Requests[^1].Uri);
        Assert.All(stub.Requests, request => Assert.Null(request.Authorization));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RemoteEndpointWithoutKeyFailsBeforeAnyRequest(string? key)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => OllamaEndpoint.Create("https://ollama.com/", key));

        Assert.Contains(OllamaEndpoint.ApiKeyEnvironmentVariable, exception.Message);
    }

    [Fact]
    public void RemoteEndpointRefusesPlainHttpSoTheKeyIsNeverSentInClearText()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => OllamaEndpoint.Create("http://ollama.com", FakeKey));

        Assert.Contains("HTTPS", exception.Message);
        Assert.DoesNotContain(FakeKey, exception.Message);
    }

    [Theory]
    [InlineData("https://user:secret-in-url@ollama.com")]
    [InlineData("https://secret-in-url@ollama.com/")]
    [InlineData("http://user:secret-in-url@127.0.0.1:11434")]
    public void AddressWithUserInfoIsRejectedWithoutEchoingIt(string address)
    {
        var exception = Assert.Throws<ArgumentException>(() => OllamaEndpoint.Create(address, FakeKey));

        Assert.Contains(OllamaEndpoint.ApiKeyEnvironmentVariable, exception.Message);
        Assert.DoesNotContain("secret-in-url", exception.Message);
    }

    [Fact]
    public void EndpointDescriptionNeverContainsTheKey()
    {
        var endpoint = OllamaEndpoint.Create("https://ollama.com", FakeKey);

        Assert.DoesNotContain(FakeKey, endpoint.ToString());
        Assert.Contains("API key set", endpoint.ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, OllamaFailureKind.RateLimited)]
    [InlineData(HttpStatusCode.Unauthorized, OllamaFailureKind.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, OllamaFailureKind.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound, OllamaFailureKind.HttpError)]
    [InlineData(HttpStatusCode.InternalServerError, OllamaFailureKind.HttpError)]
    public async Task RejectedChatIsClassifiedByStatusAndLeaksNoKey(HttpStatusCode status, OllamaFailureKind expected)
    {
        var stub = new StubHandler(status, "{\"error\":\"rejected\"}");
        var logger = new CapturingLogger<OllamaStructuredTextProvider>();
        var recorder = new OllamaRequestRecorder { InnerHandler = stub };
        using var provider = CreateTextProvider(OllamaEndpoint.Create("https://ollama.com", FakeKey), recorder, verify: false, logger);

        var exception = await Assert.ThrowsAsync<ReceiptExtractionException>(
            () => provider.GenerateAsync(TextRequest(), TestContext.Current.CancellationToken));
        var records = recorder.TakeRecords();

        Assert.Equal(expected, OllamaFailureClassifier.Classify(records, exception));
        Assert.Equal((int)status, Assert.Single(records).StatusCode);
        Assert.DoesNotContain(FakeKey, exception.ToString());
        Assert.NotEmpty(logger.Messages);
        Assert.All(logger.Messages, message => Assert.DoesNotContain(FakeKey, message));
    }

    [Fact]
    public async Task RateLimitDuringModelCheckIsClassifiedAsRateLimited()
    {
        var stub = new StubHandler(HttpStatusCode.TooManyRequests, "{}");
        var recorder = new OllamaRequestRecorder { InnerHandler = stub };
        using var provider = CreateVisionProvider(OllamaEndpoint.Create("https://ollama.com", FakeKey), recorder, verify: true);

        var status = await provider.GetStatusAsync(TestContext.Current.CancellationToken);

        Assert.False(status.Available);
        Assert.DoesNotContain(FakeKey, status.Message);
        Assert.Equal(
            OllamaFailureKind.RateLimited,
            OllamaFailureClassifier.Classify(recorder.TakeRecords(), new InvalidOperationException(status.Message)));
    }

    [Fact]
    public async Task VisionRequestIsMeasuredAndSkipsModelCheckWhenDisabled()
    {
        var stub = new StubHandler(HttpStatusCode.OK, ChatBody);
        var recorder = new OllamaRequestRecorder { InnerHandler = stub };
        using var provider = CreateVisionProvider(OllamaEndpoint.Create("https://ollama.com", FakeKey), recorder, verify: false);

        await provider.GenerateAsync(VisionRequest(), TestContext.Current.CancellationToken);

        var record = Assert.Single(recorder.TakeRecords());
        Assert.Equal("/api/chat", record.Path);
        Assert.Equal(200, record.StatusCode);
        Assert.Equal(stub.Requests.Single().BodyLength, record.RequestBytes);
        Assert.True(record.RequestBytes > 0);
        Assert.Empty(recorder.TakeRecords());
        Assert.Equal($"Bearer {FakeKey}", stub.Requests.Single().Authorization);
    }

    [Fact]
    public void TransportSuccessWithUnusableOutputIsAModelError()
    {
        IReadOnlyList<OllamaRequestRecord> records = [new("POST", "/api/chat", 200, 10, 5)];

        Assert.Equal(OllamaFailureKind.None, OllamaFailureClassifier.Classify(records, null));
        Assert.Equal(
            OllamaFailureKind.ModelError,
            OllamaFailureClassifier.Classify(records, new ReceiptExtractionException("invalid JSON")));
        Assert.Equal(
            OllamaFailureKind.Timeout,
            OllamaFailureClassifier.Classify([new("POST", "/api/chat", null, 10, 5)], new ReceiptExtractionException("Parser model 'm' timed out.")));
        Assert.Equal(
            OllamaFailureKind.Unreachable,
            OllamaFailureClassifier.Classify([new("POST", "/api/chat", null, 10, 5)], new ReceiptExtractionException("Cannot reach Ollama.")));
    }

    private const string ChatBody = """
        {"message":{"content":"{\"ok\":true}"},"done_reason":"stop","models":[{"name":"model-a"}]}
        """;

    private static OllamaStructuredTextProvider CreateTextProvider(
        OllamaEndpoint endpoint,
        HttpMessageHandler inner,
        bool verify,
        ILogger<OllamaStructuredTextProvider>? logger = null) =>
        new(
            endpoint.CreateClient(TimeSpan.FromSeconds(5), inner),
            Options.Create(new ReceiptRecognitionOptions { RowsParserModel = "model-a", VerifyModelAvailability = verify }),
            logger ?? new CapturingLogger<OllamaStructuredTextProvider>());

    private static OllamaStructuredVisionProvider CreateVisionProvider(
        OllamaEndpoint endpoint,
        HttpMessageHandler inner,
        bool verify) =>
        new(
            endpoint.CreateClient(TimeSpan.FromSeconds(5), inner),
            Options.Create(new ReceiptRecognitionOptions
            {
                Mode = ReceiptRecognitionMode.Vision,
                VisionModel = "model-a",
                VerifyModelAvailability = verify
            }),
            new CapturingLogger<OllamaStructuredVisionProvider>());

    private static StructuredTextRequest TextRequest() =>
        new("prompt", "{\"type\":\"object\"}", "p1", "s1", "correlation-1");

    private static StructuredVisionRequest VisionRequest() =>
        new([new ReceiptImageData(new byte[] { 1, 2, 3 }, "image/jpeg")], "prompt", "{\"type\":\"object\"}", "p1", "s1", "correlation-1");

    private sealed record SeenRequest(string Path, string Uri, string? Authorization, long BodyLength);

    /// <summary>Answers every path with the same status and body; the body doubles as a tags and chat response.</summary>
    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public List<SeenRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var length = request.Content is null ? 0 : (await request.Content.ReadAsByteArrayAsync(cancellationToken)).LongLength;
            Requests.Add(new(
                request.RequestUri!.AbsolutePath,
                request.RequestUri.ToString(),
                request.Headers.Authorization?.ToString(),
                length));
            return new HttpResponseMessage(status) { Content = new StringContent(body) };
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception) + (exception is null ? "" : " " + exception));
    }
}
