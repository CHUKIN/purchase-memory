using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PurchaseMemory.Domain.Products;

namespace PurchaseMemory.IntegrationTests;

/// <summary>The category endpoints through the real host, with real auth.</summary>
public sealed class CategoryApiTests
{
    private static readonly string Milk = ProductCategorySeed.IdFor(2).ToString();

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public static TheoryData<string, string> ProtectedCalls => new()
    {
        { "GET", "/api/categories" },
        { "POST", "/api/categories" },
        { "PUT", $"/api/categories/{Milk}" },
        { "DELETE", $"/api/categories/{Milk}" },
        { "PUT", $"/api/products/{Guid.NewGuid()}/classification" },
        { "GET", $"/api/products/{Guid.NewGuid()}/audit" },
        { "POST", "/api/products/categorization/backfill" },
        { "GET", $"/api/products?categoryId={Milk}" }
    };

    [Theory]
    [MemberData(nameof(ProtectedCalls))]
    public async Task EveryCategoryCallNeedsAnApprovedValidToken(string method, string path)
    {
        await using var host = new AuthTestHost();
        foreach (var token in new[]
                 {
                     null,
                     AuthTestHost.Token(key: AuthTestHost.ForeignKey),
                     AuthTestHost.Token(expires: DateTime.UtcNow.AddMinutes(-5))
                 })
        {
            using var client = host.CreateClientFor(token);
            var response = await client.SendAsync(Request(method, path), Cancel);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using var pending = host.CreateClientFor(AuthTestHost.Token(access: "pending"));
        Assert.Equal(HttpStatusCode.Forbidden, (await pending.SendAsync(Request(method, path), Cancel)).StatusCode);
    }

    [Fact]
    public async Task ApprovedUserManagesTheTreeAndTheBackfillMakesNoCallInTests()
    {
        await using var host = new AuthTestHost();
        using var client = host.CreateClientFor(AuthTestHost.Token());

        var tree = await client.GetFromJsonAsync<JsonElement>("/api/categories", Cancel);
        Assert.Equal(ProductCategorySeed.Categories.Count, tree.GetArrayLength());
        Assert.Equal("dairy", tree[0].GetProperty("systemKey").GetString());

        var created = await client.PostAsJsonAsync("/api/categories", new { name = "Кошачий корм", parentId = (Guid?)null }, Cancel);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var duplicate = await client.PostAsJsonAsync("/api/categories", new { name = "кошачий  корм" }, Cancel);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var empty = await client.PostAsJsonAsync("/api/categories", new { name = " " }, Cancel);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);

        var refused = await client.DeleteAsync($"/api/categories/{ProductCategorySeed.IdFor(1)}", Cancel);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);

        var missingProduct = await client.PutAsJsonAsync(
            $"/api/products/{Guid.NewGuid()}/classification",
            new { setCategory = true, categoryId = Milk, setBrand = false },
            Cancel);
        Assert.Equal(HttpStatusCode.NotFound, missingProduct.StatusCode);

        var backfill = await client.PostAsync("/api/products/categorization/backfill?limit=10", null, Cancel);
        var summary = await backfill.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        Assert.False(summary.GetProperty("configured").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsync("/api/products/categorization/backfill?limit=501", null, Cancel)).StatusCode);

        var badFilter = await client.GetAsync($"/api/products?categoryId={Guid.NewGuid()}", Cancel);
        Assert.Equal(HttpStatusCode.BadRequest, badFilter.StatusCode);
    }

    [Fact]
    public async Task BackfillIsRateLimitedPerUser()
    {
        await using var host = new AuthTestHost();
        using var client = host.CreateClientFor(AuthTestHost.Token());
        for (var run = 0; run < 3; run++)
        {
            Assert.Equal(HttpStatusCode.OK,
                (await client.PostAsync("/api/products/categorization/backfill?limit=10&retryAttempted=true", null, Cancel)).StatusCode);
        }

        var limited = await client.PostAsync("/api/products/categorization/backfill", null, Cancel);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.Contains("Retry-After"));
    }

    [Fact]
    public void BackfillGateLetsOneRunThrough()
    {
        var gate = new PurchaseMemory.Api.Endpoints.CategorizationBackfillGate();
        var first = gate.TryEnter();
        Assert.NotNull(first);
        Assert.Null(gate.TryEnter());
        first.Dispose();
        first.Dispose();
        using var second = gate.TryEnter();
        Assert.NotNull(second);
        Assert.Null(gate.TryEnter());
    }

    private static HttpRequestMessage Request(string method, string path)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method is "POST" or "PUT")
        {
            request.Content = JsonContent.Create(new { name = "Проба", setCategory = true, categoryId = Milk });
        }

        return request;
    }
}
