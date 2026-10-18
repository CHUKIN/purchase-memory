using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PurchaseMemory.Application.Receipts.TextLocation;
using PurchaseMemory.Infrastructure.TextLocation;

namespace PurchaseMemory.IntegrationTests;

/// <summary>
/// The Azure prebuilt-read client against a fake HTTP handler: no request
/// ever leaves the process. Waits are recorded instead of slept.
/// </summary>
public sealed class AzureTextLocatorTests
{
    private const string Endpoint = "https://pm-test.cognitiveservices.azure.com/";
    private const string Key = "test-key-not-real";
    private const string Operation = "https://pm-test.cognitiveservices.azure.com/documentintelligence/documentModels/prebuilt-read/analyzeResults/op-1?api-version=2024-11-30";

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ReadsThePageAfterPollingUntilSucceeded()
    {
        var handler = new FakeHandler(
            _ => Accepted(),
            _ => Json(HttpStatusCode.OK, """{"status":"running"}"""),
            _ => Json(HttpStatusCode.OK, Succeeded()));
        var (locator, waits) = Create(handler);

        var result = await locator.ReadAsync(new byte[] { 1, 2, 3 }, "image/jpeg", Cancel);

        Assert.Null(result.Failure);
        var page = Assert.IsType<ReceiptTextPage>(result.Page);
        Assert.Equal(1000, page.Width);
        Assert.Equal(2000, page.Height);
        var line = Assert.Single(page.Lines);
        Assert.Equal("MLEKO 3,2% 4,49 C", line.Text);
        Assert.Equal(["MLEKO", "3,2%", "4,49", "C"], line.Words.Select(word => word.Text));
        Assert.Equal("azure:prebuilt-read:2024-11-30", page.Source);

        Assert.Equal(3, handler.Requests.Count);
        var post = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, post.Method);
        Assert.Equal(
            "https://pm-test.cognitiveservices.azure.com/documentintelligence/documentModels/prebuilt-read:analyze?api-version=2024-11-30",
            post.Uri.ToString());
        Assert.Equal([1, 2, 3], post.Body);
        Assert.All(handler.Requests, request => Assert.Equal(Key, request.Key));
        Assert.Contains(TimeSpan.FromMilliseconds(3000), waits);
    }

    [Fact]
    public async Task WaitsOutRetryAfterOnRateLimitAndThenSucceeds()
    {
        var handler = new FakeHandler(
            _ => RateLimited(7),
            _ => Accepted(),
            _ => Json(HttpStatusCode.OK, Succeeded()));
        var (locator, waits) = Create(handler);

        var result = await locator.ReadAsync(new byte[] { 1 }, "image/jpeg", Cancel);

        Assert.NotNull(result.Page);
        Assert.Contains(TimeSpan.FromSeconds(7), waits);
        Assert.Equal(2, handler.Requests.Count(request => request.Method == HttpMethod.Post));
    }

    [Fact]
    public async Task GivesUpForNowWhenStillRateLimited()
    {
        var handler = new FakeHandler(_ => RateLimited(5));
        var (locator, _) = Create(handler);

        var result = await locator.ReadAsync(new byte[] { 1 }, "image/jpeg", Cancel);

        Assert.Equal(ReceiptTextLocatorFailure.RateLimited, result.Failure);
        Assert.Equal(TimeSpan.FromSeconds(5), result.RetryAfter);
        Assert.Equal(4, handler.Requests.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, ReceiptTextLocatorFailure.NotAuthorized)]
    [InlineData(HttpStatusCode.Forbidden, ReceiptTextLocatorFailure.NotAuthorized)]
    [InlineData(HttpStatusCode.NotFound, ReceiptTextLocatorFailure.NotAuthorized)]
    [InlineData(HttpStatusCode.BadRequest, ReceiptTextLocatorFailure.Rejected)]
    [InlineData(HttpStatusCode.UnsupportedMediaType, ReceiptTextLocatorFailure.Rejected)]
    [InlineData(HttpStatusCode.InternalServerError, ReceiptTextLocatorFailure.Transient)]
    [InlineData(HttpStatusCode.ServiceUnavailable, ReceiptTextLocatorFailure.Transient)]
    public async Task ClassifiesAnalyzeFailures(HttpStatusCode status, ReceiptTextLocatorFailure expected)
    {
        var handler = new FakeHandler(_ => Json(status, """{"error":{"code":"InvalidRequest","message":"secret detail","innererror":{"code":"InvalidContent"}}}"""));
        var (locator, _) = Create(handler);

        var result = await locator.ReadAsync(new byte[] { 1 }, "image/jpeg", Cancel);

        Assert.Equal(expected, result.Failure);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task AFailedAnalysisIsARejectedPhoto()
    {
        var handler = new FakeHandler(
            _ => Accepted(),
            _ => Json(HttpStatusCode.OK, """{"status":"failed","error":{"code":"InvalidRequest","innererror":{"code":"InvalidContent"}}}"""));
        var (locator, _) = Create(handler);

        var result = await locator.ReadAsync(new byte[] { 1 }, "image/jpeg", Cancel);

        Assert.Equal(ReceiptTextLocatorFailure.Rejected, result.Failure);
    }

    [Fact]
    public async Task NeverSendsTheKeyToAnotherHost()
    {
        var handler = new FakeHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Accepted);
            response.Headers.Add("Operation-Location", "https://attacker.example/steal");
            return response;
        });
        var (locator, _) = Create(handler);

        var result = await locator.ReadAsync(new byte[] { 1 }, "image/jpeg", Cancel);

        Assert.Equal(ReceiptTextLocatorFailure.Transient, result.Failure);
        Assert.Single(handler.Requests);
        Assert.DoesNotContain(handler.Requests, request => request.Uri.Host == "attacker.example");
    }

    [Theory]
    [InlineData("""{"status":"succeeded","analyzeResult":{"pages":[{"width":10""")]
    [InlineData("not json at all")]
    public async Task AMalformedAnswerIsAPassingFailureNotAnException(string body)
    {
        var handler = new FakeHandler(_ => Accepted(), _ => Json(HttpStatusCode.OK, body));
        var (locator, _) = Create(handler);

        var result = await locator.ReadAsync(new byte[] { 1 }, "image/jpeg", Cancel);

        Assert.Equal(ReceiptTextLocatorFailure.Transient, result.Failure);
    }

    [Fact]
    public async Task ABrokenResponseStreamIsAPassingFailure()
    {
        var handler = new FakeHandler(_ => Accepted(), _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new BrokenContent() });
        var (locator, _) = Create(handler);

        var result = await locator.ReadAsync(new byte[] { 1 }, "image/jpeg", Cancel);

        Assert.Equal(ReceiptTextLocatorFailure.Transient, result.Failure);
    }

    [Fact]
    public async Task ARedirectIsNotFollowedAndIsAPassingFailure()
    {
        var handler = new FakeHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Redirect);
            response.Headers.Location = new Uri("https://attacker.example/analyze");
            return response;
        });
        var (locator, _) = Create(handler);

        var result = await locator.ReadAsync(new byte[] { 1 }, "image/jpeg", Cancel);

        Assert.Equal(ReceiptTextLocatorFailure.Transient, result.Failure);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task TheApiRegistersTheClientWithoutAutomaticRedirects()
    {
        await using var host = new AuthTestHost();
        _ = host.Server;
        var factory = host.Services.GetRequiredService<IHttpMessageHandlerFactory>();
        HttpMessageHandler handler = factory.CreateHandler(AzureDocumentIntelligenceTextLocator.HttpClientName);
        while (handler is DelegatingHandler delegating)
        {
            handler = delegating.InnerHandler!;
        }

        var primary = Assert.IsType<SocketsHttpHandler>(handler);
        Assert.False(primary.AllowAutoRedirect);
        Assert.IsType<AzureDocumentIntelligenceTextLocator>(host.Services.GetRequiredService<IReceiptTextLocator>());
    }

    [Fact]
    public async Task ATimeoutIsAPassingFailure()
    {
        var handler = new FakeHandler(_ => throw new TaskCanceledException("timeout", new TimeoutException()));
        var (locator, _) = Create(handler);

        var result = await locator.ReadAsync(new byte[] { 1 }, "image/jpeg", Cancel);

        Assert.Equal(ReceiptTextLocatorFailure.Transient, result.Failure);
    }

    [Fact]
    public async Task PollingThatNeverFinishesIsAPassingFailure()
    {
        var handler = new FakeHandler(request => request.Method == HttpMethod.Post
            ? Accepted()
            : Json(HttpStatusCode.OK, """{"status":"running"}"""));
        var (locator, _) = Create(handler);

        var result = await locator.ReadAsync(new byte[] { 1 }, "image/jpeg", Cancel);

        Assert.Equal(ReceiptTextLocatorFailure.Transient, result.Failure);
        Assert.Equal(1 + 30, handler.Requests.Count);
    }

    [Theory]
    [InlineData(null, Key)]
    [InlineData(Endpoint, null)]
    [InlineData("", Key)]
    [InlineData("http://pm-test.cognitiveservices.azure.com/", Key)]
    [InlineData("not a url", Key)]
    public async Task WithoutAUsableConfigurationNothingIsSent(string? endpoint, string? key)
    {
        var handler = new FakeHandler(_ => throw new InvalidOperationException("must not be called"));
        var (locator, _) = Create(handler, endpoint, key);

        Assert.False(locator.IsConfigured);
        var result = await locator.ReadAsync(new byte[] { 1 }, "image/jpeg", Cancel);

        Assert.Equal(ReceiptTextLocatorFailure.NotAuthorized, result.Failure);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void ParsesARealCachedResponse()
    {
        var layout = ReceiptTextMatcherTests.LoadLayout("04");
        Assert.True(layout.Lines.Count > 30);
        Assert.All(layout.Lines, line => Assert.Equal(8, line.Polygon.Count));
        Assert.True(layout.Lines.Sum(line => line.Words.Count) > layout.Lines.Count);
    }

    private static (AzureDocumentIntelligenceTextLocator Locator, List<TimeSpan> Waits) Create(
        FakeHandler handler,
        string? endpoint = Endpoint,
        string? key = Key)
    {
        var waits = new List<TimeSpan>();
        var locator = new AzureDocumentIntelligenceTextLocator(
            new HttpClient(handler),
            Options.Create(new AzureDocumentIntelligenceOptions()),
            NullLogger<AzureDocumentIntelligenceTextLocator>.Instance,
            () => endpoint,
            () => key,
            (delay, _) =>
            {
                waits.Add(delay);
                return Task.CompletedTask;
            });
        return (locator, waits);
    }

    private static HttpResponseMessage Accepted()
    {
        var response = new HttpResponseMessage(HttpStatusCode.Accepted);
        response.Headers.Add("Operation-Location", Operation);
        return response;
    }

    private static HttpResponseMessage RateLimited(int seconds)
    {
        var response = Json(HttpStatusCode.TooManyRequests, """{"error":{"code":"429"}}""");
        response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));
        return response;
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static string Succeeded() => """
        {
          "status": "succeeded",
          "analyzeResult": {
            "content": "MLEKO 3,2% 4,49 C",
            "pages": [{
              "pageNumber": 1, "width": 1000, "height": 2000, "unit": "pixel",
              "words": [
                {"content": "MLEKO", "polygon": [10,10,90,10,90,30,10,30], "span": {"offset": 0, "length": 5}},
                {"content": "3,2%", "polygon": [100,10,140,10,140,30,100,30], "span": {"offset": 6, "length": 4}},
                {"content": "4,49", "polygon": [600,10,660,10,660,30,600,30], "span": {"offset": 11, "length": 4}},
                {"content": "C", "polygon": [680,10,690,10,690,30,680,30], "span": {"offset": 16, "length": 1}}
              ],
              "lines": [
                {"content": "MLEKO 3,2% 4,49 C", "polygon": [10,10,690,10,690,30,10,30], "spans": [{"offset": 0, "length": 17}]}
              ]
            }]
          }
        }
        """;

    /// <summary>A body that breaks off while being read, like a dropped connection.</summary>
    private sealed class BrokenContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) =>
            throw new IOException("connection reset");

        protected override Task<Stream> CreateContentReadStreamAsync() => throw new IOException("connection reset");

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed record SentRequest(HttpMethod Method, Uri Uri, string? Key, byte[] Body);

    private sealed class FakeHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responses) : HttpMessageHandler
    {
        public List<SentRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            Requests.Add(new SentRequest(
                request.Method,
                request.RequestUri!,
                request.Headers.TryGetValues("Ocp-Apim-Subscription-Key", out var keys) ? keys.Single() : null,
                body));
            var respond = responses[Math.Min(Requests.Count - 1, responses.Length - 1)];
            return respond(request);
        }
    }
}
