using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using PurchaseMemory.Application.Products.Media;
using PurchaseMemory.Domain.Products;
using PurchaseMemory.Infrastructure.OpenFoodFacts;

namespace PurchaseMemory.IntegrationTests;

/// <summary>
/// Barcode normalization (the same rules as <c>barcode.ts</c>) and the Open
/// Food Facts client: parsing, safe image URLs, the timeout and the cache.
/// </summary>
public sealed class BarcodeAndOpenFoodFactsTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("5900512320045", "5900512320045")]
    [InlineData(" 5 900512-320045 ", "5900512320045")]
    [InlineData("96385074", "96385074")]
    // UPC-A becomes the EAN-13 it equals.
    [InlineData("036000291452", "0036000291452")]
    // UPC-E 0 425261 4 expands to UPC-A 042100005264.
    [InlineData("04252614", "0042100005264")]
    [InlineData("5900512320046", null)]
    [InlineData("590051232004", null)]
    [InlineData("12345", null)]
    [InlineData("59005123200a5", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void NormalizesShopBarcodes(string? raw, string? expected)
    {
        Assert.Equal(expected, Barcode.TryNormalize(raw));
    }

    [Theory]
    [InlineData("https://images.openfoodfacts.org/images/products/590/front.jpg", true)]
    [InlineData("https://openfoodfacts.org/x.jpg", true)]
    [InlineData("http://images.openfoodfacts.org/x.jpg", false)]
    [InlineData("https://openfoodfacts.org.evil.example/x.jpg", false)]
    [InlineData("https://evilopenfoodfacts.org/x.jpg", false)]
    [InlineData("https://user@images.openfoodfacts.org/x.jpg", false)]
    [InlineData("javascript:alert(1)", false)]
    public void KeepsOnlyOpenFoodFactsHttpsImages(string url, bool kept)
    {
        Assert.Equal(kept, OpenFoodFactsProduct.SafeImageUrl(url) is not null);
    }

    [Fact]
    public async Task AKnownProductIsParsedAndCached()
    {
        var handler = new StubHandler(_ => Json("""
            {"status":1,"product":{"product_name":" Kefir naturalny ","brands":"Bakoma","quantity":"400 g",
             "image_front_small_url":"https://images.openfoodfacts.org/images/products/590/front_pl.200.jpg"}}
            """));
        var client = Client(handler);

        var first = await client.LookupAsync("5900512320045", Cancel);
        var second = await client.LookupAsync("5900512320045", Cancel);

        Assert.Equal(OpenFoodFactsStatus.Found, first.Status);
        Assert.Equal(new OpenFoodFactsProduct("Kefir naturalny", "Bakoma", "400 g", "https://images.openfoodfacts.org/images/products/590/front_pl.200.jpg"), first.Product);
        Assert.Same(first, second);
        Assert.Equal(1, handler.Calls);
        Assert.Equal("/api/v2/product/5900512320045", handler.LastUri!.AbsolutePath);
        // Nothing but the code and the field list goes out.
        Assert.Equal("?fields=product_name,brands,image_front_small_url,quantity", handler.LastUri.Query);
    }

    [Fact]
    public async Task UnknownCodesAreCachedButErrorsAreNot()
    {
        var notFound = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = Client(notFound);
        Assert.Equal(OpenFoodFactsStatus.NotFound, (await client.LookupAsync("96385074", Cancel)).Status);
        Assert.Equal(OpenFoodFactsStatus.NotFound, (await client.LookupAsync("96385074", Cancel)).Status);
        Assert.Equal(1, notFound.Calls);

        var statusZero = Client(new StubHandler(_ => Json("""{"status":0,"status_verbose":"product not found"}""")));
        Assert.Equal(OpenFoodFactsStatus.NotFound, (await statusZero.LookupAsync("96385074", Cancel)).Status);
        var noName = Client(new StubHandler(_ => Json("""{"status":1,"product":{"quantity":"1 l"}}""")));
        Assert.Equal(OpenFoodFactsStatus.NotFound, (await noName.LookupAsync("96385074", Cancel)).Status);

        var failing = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var flaky = Client(failing);
        Assert.Equal(OpenFoodFactsStatus.Error, (await flaky.LookupAsync("96385074", Cancel)).Status);
        Assert.Equal(OpenFoodFactsStatus.Error, (await flaky.LookupAsync("96385074", Cancel)).Status);
        Assert.Equal(2, failing.Calls);

        var broken = Client(new StubHandler(_ => Json("not json")));
        Assert.Equal(OpenFoodFactsStatus.Error, (await broken.LookupAsync("96385074", Cancel)).Status);
        var unreachable = Client(new StubHandler(_ => throw new HttpRequestException("no route")));
        Assert.Equal(OpenFoodFactsStatus.Error, (await unreachable.LookupAsync("96385074", Cancel)).Status);
    }

    [Fact]
    public async Task ASlowAnswerBecomesAnErrorAfterTheTimeout()
    {
        var slow = new StubHandler(_ => Json("{}"), delay: TimeSpan.FromSeconds(30));
        var client = Client(slow);
        var started = DateTime.UtcNow;

        var result = await client.LookupAsync("96385074", Cancel);

        Assert.Equal(OpenFoodFactsStatus.Error, result.Status);
        Assert.InRange(DateTime.UtcNow - started, TimeSpan.Zero, OpenFoodFactsClient.Timeout + TimeSpan.FromSeconds(3));
    }

    private static OpenFoodFactsClient Client(StubHandler handler) =>
        new(
            new HttpClient(handler) { BaseAddress = OpenFoodFactsClient.BaseAddress },
            new OpenFoodFactsCache(TimeProvider.System),
            NullLogger<OpenFoodFactsClient>.Instance);

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond, TimeSpan? delay = null) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        public Uri? LastUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastUri = request.RequestUri;
            if (delay is { } wait)
            {
                await Task.Delay(wait, cancellationToken);
            }

            return respond(request);
        }
    }
}
