using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PurchaseMemory.Application.Purchases;
using PurchaseMemory.Application.Receipts;
using PurchaseMemory.Application.ReviewQueue;
using PurchaseMemory.Domain.Products;
using PurchaseMemory.Domain.Receipts;
using PurchaseMemory.Infrastructure.Persistence;

namespace PurchaseMemory.IntegrationTests;

/// <summary>
/// Correcting an already saved purchase over HTTP: header, lines, product links,
/// validation, concurrency, access, and the review queue item going away.
/// </summary>
public sealed class PurchaseEditTests
{
    private static readonly DateTimeOffset July1 = new(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task FixingATotalMismatchUpdatesPurchaseAndReceiptAndLeavesTheQueue()
    {
        await using var host = new AuthTestHost();
        var (receiptId, purchaseId) = await SaveReceiptAsync(host.Services, "Żabka", 20.00m, ("KAWA", 1, 15.00m, 15.00m));
        Assert.NotNull(await FindQueueItemAsync(host.Services, ReviewQueueItemKind.TotalMismatch, purchaseId));
        using var client = host.CreateClientFor(AuthTestHost.Token());

        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/purchases/{purchaseId}", Cancel);
        Assert.Equal(receiptId, detail.GetProperty("sourceReceiptId").GetGuid());
        Assert.Equal("receiptTotalMismatch", detail.GetProperty("issues")[0].GetProperty("type").GetString());
        Assert.False(detail.GetProperty("items")[0].GetProperty("productId").ValueKind == JsonValueKind.Null);

        var body = Snapshot(detail);
        body["items"]![0]!["unitPrice"] = 20.00m;
        body["items"]![0]!["lineTotal"] = 20.00m;
        var response = await client.PutAsJsonAsync($"/api/purchases/{purchaseId}", body, Cancel);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        Assert.Empty(updated.GetProperty("issues").EnumerateArray());
        Assert.Equal(20.00m, updated.GetProperty("items")[0].GetProperty("lineTotal").GetDecimal());
        Assert.Null(await FindQueueItemAsync(host.Services, ReviewQueueItemKind.TotalMismatch, purchaseId));

        // The receipt page and its audit trail show the correction and who made it.
        var receipt = await client.GetFromJsonAsync<JsonElement>($"/api/receipts/{receiptId}", Cancel);
        Assert.Equal(20.00m, receipt.GetProperty("snapshot").GetProperty("items")[0].GetProperty("lineTotal").GetDecimal());
        var audit = await client.GetFromJsonAsync<JsonElement>($"/api/receipts/{receiptId}/audit", Cancel);
        var lineTotal = audit.EnumerateArray().Single(entry => entry.GetProperty("fieldName").GetString() == "Item:0.LineTotal");
        Assert.Equal("11111111-1111-1111-1111-111111111111", lineTotal.GetProperty("actor").GetString());
        Assert.Equal("anna@example.com", lineTotal.GetProperty("actorEmail").GetString());
        // Keeping the existing product link is not an edit.
        Assert.DoesNotContain(audit.EnumerateArray(), entry => entry.GetProperty("fieldName").GetString() == "Item:0.Product");

        // A second edit from the returned state is accepted (no false conflict).
        var again = Snapshot(updated);
        again["merchantName"] = "Żabka Nano";
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/purchases/{purchaseId}", again, Cancel)).StatusCode);
    }

    [Fact]
    public async Task LinesCanBeAddedRemovedAndPricedWhichClearsMissingPrice()
    {
        await using var host = new AuthTestHost();
        var purchase = await SaveAsync(host.Services, "Lidl", 12.00m, ("CHLEB", 1, null, null), ("MASŁO", 1, null, null), ("JAJA", 1, 9.99m, 9.99m));
        var queued = await FindQueueItemAsync(host.Services, ReviewQueueItemKind.MissingPrice, purchase.PurchaseId);
        Assert.Equal(0, queued!.ItemPosition);
        using var client = host.CreateClientFor(AuthTestHost.Token());

        var response = await client.PutAsJsonAsync($"/api/purchases/{purchase.PurchaseId}", new
        {
            expectedUpdatedAt = purchase.UpdatedAt,
            merchantName = "Lidl",
            purchasedAt = July1,
            currency = "pln",
            receiptTotal = 12.00m,
            items = new object[]
            {
                new { name = "CHLEB", quantity = 1, unitPrice = 4.00m, lineTotal = 4.00m },
                new { name = "JAJA", quantity = 1, unitPrice = 9.99m, lineTotal = 9.99m },
                new { name = "RABAT JAJA", lineTotal = -1.99m, kind = "discount" }
            }
        }, Cancel);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        Assert.Equal("PLN", updated.GetProperty("currency").GetString());
        var items = updated.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(["CHLEB", "JAJA", "RABAT JAJA"], items.Select(item => item.GetProperty("name").GetString()));
        Assert.Equal("discount", items[2].GetProperty("kind").GetString());
        Assert.Equal(1, items[2].GetProperty("discountedItemPosition").GetInt32());
        Assert.Empty(updated.GetProperty("issues").EnumerateArray());
        Assert.Null(await FindQueueItemAsync(host.Services, ReviewQueueItemKind.MissingPrice, purchase.PurchaseId));
        Assert.Null(await FindQueueItemAsync(host.Services, ReviewQueueItemKind.TotalMismatch, purchase.PurchaseId));
    }

    [Fact]
    public async Task PendingMatchIsResolvedByLinkingAnExistingProductAndTheStoreRemembersIt()
    {
        await using var host = new AuthTestHost();
        await SaveAsync(host.Services, "Biedronka", null, ("Ser Gouda plastry 150g", 1, 5m, 5m));
        var second = await SaveAsync(host.Services, "Biedronka", null, ("Ser Gouda plastry 250g", 1, 7m, 7m));
        var queued = await FindQueueItemAsync(host.Services, ReviewQueueItemKind.PendingMatch, second.PurchaseId);
        Assert.Equal(0, queued!.ItemPosition);
        using var client = host.CreateClientFor(AuthTestHost.Token());

        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/purchases/{second.PurchaseId}", Cancel);
        var line = detail.GetProperty("items")[0];
        Assert.True(line.GetProperty("pendingMatch").GetBoolean());
        Assert.Equal(JsonValueKind.Null, line.GetProperty("productId").ValueKind);
        var candidate = line.GetProperty("candidates")[0];
        Assert.Equal("Ser Gouda plastry 150g", candidate.GetProperty("name").GetString());
        var productId = candidate.GetProperty("productId").GetGuid();

        var body = Snapshot(detail);
        body["items"]![0]!["productLink"] = "existing";
        body["items"]![0]!["productId"] = productId;
        var response = await client.PutAsJsonAsync($"/api/purchases/{second.PurchaseId}", body, Cancel);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        Assert.Equal(productId, updated.GetProperty("items")[0].GetProperty("productId").GetGuid());
        Assert.False(updated.GetProperty("items")[0].GetProperty("pendingMatch").GetBoolean());
        Assert.Null(await FindQueueItemAsync(host.Services, ReviewQueueItemKind.PendingMatch, second.PurchaseId));

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>();
            var alias = await dbContext.ProductAliases.SingleAsync(alias => alias.RawText == "Ser Gouda plastry 250g", Cancel);
            Assert.Equal(productId, alias.ProductId);
            Assert.Equal(ProductAliasSource.HumanConfirmed, alias.Source);
        }

        // The next receipt from the same store follows the person's decision.
        var third = await SaveAsync(host.Services, "Biedronka", null, ("Ser Gouda plastry 250g", 1, 7m, 7m));
        Assert.Null(await FindQueueItemAsync(host.Services, ReviewQueueItemKind.PendingMatch, third.PurchaseId));
    }

    [Fact]
    public async Task ALineCanGetABrandNewProduct()
    {
        await using var host = new AuthTestHost();
        await SaveAsync(host.Services, "Biedronka", null, ("Ser Gouda plastry 150g", 1, 5m, 5m));
        var second = await SaveAsync(host.Services, "Biedronka", null, ("Ser Gouda plastry 250g", 1, 7m, 7m));
        using var client = host.CreateClientFor(AuthTestHost.Token());
        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/purchases/{second.PurchaseId}", Cancel);

        var body = Snapshot(detail);
        body["items"]![0]!["productLink"] = "new";
        var updated = await (await client.PutAsJsonAsync($"/api/purchases/{second.PurchaseId}", body, Cancel))
            .Content.ReadFromJsonAsync<JsonElement>(Cancel);

        var line = updated.GetProperty("items")[0];
        Assert.Equal("Ser Gouda plastry 250g", line.GetProperty("productName").GetString());
        Assert.NotEqual(detail.GetProperty("items")[0].GetProperty("candidates")[0].GetProperty("productId").GetGuid(), line.GetProperty("productId").GetGuid());
        Assert.Null(await FindQueueItemAsync(host.Services, ReviewQueueItemKind.PendingMatch, second.PurchaseId));
    }

    [Fact]
    public async Task InvalidCorrectionsAreRejectedWithClearErrorsAndChangeNothing()
    {
        await using var host = new AuthTestHost();
        var purchase = await SaveAsync(host.Services, "Lidl", 5m, ("MLEKO", 1, 5m, 5m));
        using var client = host.CreateClientFor(AuthTestHost.Token());
        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/purchases/{purchase.PurchaseId}", Cancel);
        var url = $"/api/purchases/{purchase.PurchaseId}";

        await AssertBadRequestAsync(client, url, Change(detail, body => body.Remove("expectedUpdatedAt")), "expectedUpdatedAt");
        await AssertBadRequestAsync(client, url, Change(detail, body => body["items"] = new JsonArray()), "at least one line");
        await AssertBadRequestAsync(client, url, Change(detail, body => body["items"]![0]!["name"] = "  "), "Line 1");
        await AssertBadRequestAsync(client, url, Change(detail, body => body["currency"] = "ZŁOTY"), "Currency");
        await AssertBadRequestAsync(client, url, Change(detail, body => body["receiptTotal"] = -1m), "negative");
        await AssertBadRequestAsync(client, url, Change(detail, body => body["items"]![0]!["quantity"] = 0m), "Quantity");
        await AssertBadRequestAsync(client, url, Change(detail, body => body["items"]![0]!["kind"] = "gift"), "Line 1");
        await AssertBadRequestAsync(client, url, Change(detail, body => body["items"]![0]!["productLink"] = "maybe"), "productLink");
        await AssertBadRequestAsync(client, url, Change(detail, body =>
        {
            body["items"]![0]!["productLink"] = "existing";
            body["items"]![0]!["productId"] = Guid.NewGuid();
        }), "was not found");
        await AssertBadRequestAsync(client, url, Change(detail, body =>
        {
            body["items"]!.AsArray().Add(new JsonObject { ["name"] = "RABAT", ["lineTotal"] = -1m, ["kind"] = "discount", ["productLink"] = "new" });
        }), "discount line cannot be linked");

        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"/api/purchases/{Guid.NewGuid()}", Snapshot(detail), Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/purchases/{Guid.NewGuid()}", Cancel)).StatusCode);

        var unchanged = await client.GetFromJsonAsync<JsonElement>(url, Cancel);
        Assert.Equal(detail.GetProperty("updatedAt").GetDateTimeOffset(), unchanged.GetProperty("updatedAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task AStaleEditIsRefusedWithConflict()
    {
        await using var host = new AuthTestHost();
        var purchase = await SaveAsync(host.Services, "Lidl", 5m, ("MLEKO", 1, 5m, 5m));
        using var client = host.CreateClientFor(AuthTestHost.Token());
        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/purchases/{purchase.PurchaseId}", Cancel);
        var url = $"/api/purchases/{purchase.PurchaseId}";

        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(url, Change(detail, body => body["merchantName"] = "Lidl A"), Cancel)).StatusCode);
        var stale = await client.PutAsJsonAsync(url, Change(detail, body => body["merchantName"] = "Lidl B"), Cancel);

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var current = await client.GetFromJsonAsync<JsonElement>(url, Cancel);
        Assert.Equal("Lidl A", current.GetProperty("merchantName").GetString());
    }

    [Fact]
    public async Task EditingNeedsAnApprovedValidToken()
    {
        await using var host = new AuthTestHost();
        var purchase = await SaveAsync(host.Services, "Lidl", 5m, ("MLEKO", 1, 5m, 5m));
        var url = $"/api/purchases/{purchase.PurchaseId}";
        var body = new { expectedUpdatedAt = purchase.UpdatedAt, merchantName = "Hacked", items = new[] { new { name = "X", lineTotal = 1m } } };

        foreach (var token in new[]
                 {
                     null,
                     AuthTestHost.Token(key: AuthTestHost.ForeignKey),
                     AuthTestHost.Token(expires: DateTime.UtcNow.AddMinutes(-5))
                 })
        {
            using var client = host.CreateClientFor(token);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(url, Cancel)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PutAsJsonAsync(url, body, Cancel)).StatusCode);
        }

        foreach (var access in new[] { "pending", "blocked" })
        {
            using var client = host.CreateClientFor(AuthTestHost.Token(access: access));
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(url, Cancel)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync(url, body, Cancel)).StatusCode);
        }

        using var approved = host.CreateClientFor(AuthTestHost.Token());
        var current = await approved.GetFromJsonAsync<JsonElement>(url, Cancel);
        Assert.Equal("Lidl", current.GetProperty("merchantName").GetString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ShiftingLinesKeepsLinksWithoutPromotingAliasesOrAuditingProducts(bool removeFirst)
    {
        await using var host = new AuthTestHost();
        var (receiptId, purchaseId) = await SaveReceiptAsync(host.Services, "Lidl", 12.00m,
            ("MLEKO", 1, 3.00m, 3.00m), ("SER ZOLTY", 1, 6.00m, 6.00m), ("POMIDORY", 1, 3.00m, 3.00m));
        using var client = host.CreateClientFor(AuthTestHost.Token());
        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/purchases/{purchaseId}", Cancel);
        var linksBefore = detail.GetProperty("items").EnumerateArray()
            .ToDictionary(item => item.GetProperty("name").GetString()!, item => item.GetProperty("productId").GetGuid());

        var body = Snapshot(detail);
        var items = body["items"]!.AsArray();
        if (removeFirst)
        {
            items.RemoveAt(0);
            body["receiptTotal"] = 9.00m;
        }
        else
        {
            items.Insert(0, new JsonObject { ["name"] = "CHLEB", ["quantity"] = 1m, ["unitPrice"] = 4.00m, ["lineTotal"] = 4.00m, ["productLink"] = "auto" });
            body["receiptTotal"] = 16.00m;
        }

        var response = await client.PutAsJsonAsync($"/api/purchases/{purchaseId}", body, Cancel);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        foreach (var item in updated.GetProperty("items").EnumerateArray())
        {
            var name = item.GetProperty("name").GetString()!;
            if (linksBefore.TryGetValue(name, out var productId))
            {
                Assert.Equal(productId, item.GetProperty("productId").GetGuid());
            }
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>();
            Assert.DoesNotContain(await dbContext.ProductAliases.ToListAsync(Cancel), alias => alias.Source == ProductAliasSource.HumanConfirmed);
        }

        var audit = await client.GetFromJsonAsync<JsonElement>($"/api/receipts/{receiptId}/audit", Cancel);
        Assert.DoesNotContain(audit.EnumerateArray(), entry => entry.GetProperty("fieldName").GetString()!.EndsWith(".Product", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("{\"expectedUpdatedAt\": \"yesterday\", \"items\": []}")]
    [InlineData("{\"expectedUpdatedAt\": \"2026-07-01T10:00:00Z\", \"receiptTotal\": \"lots\", \"items\": [{\"name\": \"X\"}]}")]
    [InlineData("{\"expectedUpdatedAt\": \"2026-07-01T10:00:00Z\", \"items\": [{\"name\": \"X\", \"productId\": 42}]}")]
    public async Task MalformedBodiesAreBadRequests(string json)
    {
        await using var host = new AuthTestHost();
        var purchase = await SaveAsync(host.Services, "Lidl", 5m, ("MLEKO", 1, 5m, 5m));
        using var client = host.CreateClientFor(AuthTestHost.Token());

        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        var response = await client.PutAsync($"/api/purchases/{purchase.PurchaseId}", content, Cancel);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private static async Task AssertBadRequestAsync(HttpClient client, string url, JsonObject body, string expectedText)
    {
        var response = await client.PutAsJsonAsync(url, body, Cancel);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        var detail = problem.TryGetProperty("detail", out var value) ? value.GetString() ?? string.Empty : problem.ToString();
        Assert.Contains(expectedText, detail, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The detail response turned into a PUT body, the way the editor sends it back.</summary>
    private static JsonObject Snapshot(JsonElement detail)
    {
        var items = new JsonArray();
        foreach (var item in detail.GetProperty("items").EnumerateArray())
        {
            var productId = item.GetProperty("productId");
            items.Add(new JsonObject
            {
                ["name"] = item.GetProperty("name").GetString(),
                ["quantity"] = JsonNode.Parse(item.GetProperty("quantity").GetRawText()),
                ["unit"] = JsonNode.Parse(item.GetProperty("unit").GetRawText()),
                ["unitPrice"] = JsonNode.Parse(item.GetProperty("unitPrice").GetRawText()),
                ["lineTotal"] = JsonNode.Parse(item.GetProperty("lineTotal").GetRawText()),
                ["kind"] = item.GetProperty("kind").GetString(),
                ["productLink"] = productId.ValueKind == JsonValueKind.Null ? "auto" : "existing",
                ["productId"] = JsonNode.Parse(productId.GetRawText())
            });
        }

        return new JsonObject
        {
            ["expectedUpdatedAt"] = detail.GetProperty("updatedAt").GetString(),
            ["merchantName"] = JsonNode.Parse(detail.GetProperty("merchantName").GetRawText()),
            ["purchasedAt"] = JsonNode.Parse(detail.GetProperty("purchasedAt").GetRawText()),
            ["currency"] = JsonNode.Parse(detail.GetProperty("currency").GetRawText()),
            ["receiptTotal"] = JsonNode.Parse(detail.GetProperty("receiptTotal").GetRawText()),
            ["items"] = items
        };
    }

    private static JsonObject Change(JsonElement detail, Action<JsonObject> change)
    {
        var body = Snapshot(detail);
        change(body);
        return body;
    }

    private static async Task<ReviewQueueItem?> FindQueueItemAsync(IServiceProvider services, ReviewQueueItemKind kind, Guid purchaseId)
    {
        await using var scope = services.CreateAsyncScope();
        var queue = await scope.ServiceProvider.GetRequiredService<IReviewQueueService>().GetAsync(100, Cancel);
        return queue.Groups.Where(group => group.Kind == kind).SelectMany(group => group.Items)
            .SingleOrDefault(item => item.PurchaseId == purchaseId);
    }

    private static async Task<PurchaseDraftView> SaveAsync(
        IServiceProvider services,
        string merchant,
        decimal? total,
        params (string Name, decimal? Quantity, decimal? UnitPrice, decimal? LineTotal)[] items)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IPurchaseDraftService>().CreateAsync(
            new CreatePurchaseDraftCommand(
                merchant,
                July1,
                "PLN",
                total,
                items.Select(item => new PurchaseItemCommand(item.Name, item.Quantity, "szt", item.UnitPrice, item.LineTotal)).ToList(),
                AllowPossibleDuplicate: true),
            Cancel);
    }

    private static async Task<(Guid ReceiptId, Guid PurchaseId)> SaveReceiptAsync(
        IServiceProvider services,
        string merchant,
        decimal total,
        params (string Name, decimal Quantity, decimal UnitPrice, decimal LineTotal)[] items)
    {
        var receipt = ReceiptCapture.CreateUploaded(
            Guid.NewGuid(),
            [ReceiptImagePart.Create(0, $"receipts/{Guid.NewGuid():N}.jpg", "image/jpeg", 10,
                Convert.ToHexStringLower(Guid.NewGuid().ToByteArray()) + Convert.ToHexStringLower(Guid.NewGuid().ToByteArray()))],
            July1);
        receipt.Queue(July1);
        receipt.StartProcessing(July1);
        receipt.CompleteProcessing(merchant, July1, "PLN", total,
            items.Select((item, position) => ReceiptCaptureItem.Create(position, item.Name, item.Quantity, "szt", item.UnitPrice, item.LineTotal)).ToList(),
            "test", null, July1);

        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>();
        dbContext.ReceiptCaptures.Add(receipt);
        await dbContext.SaveChangesAsync(Cancel);
        var purchase = await scope.ServiceProvider.GetRequiredService<IReceiptProcessingService>()
            .SaveAsync(new SaveReceiptCommand(receipt.Id), Cancel);
        return (receipt.Id, purchase.PurchaseId);
    }
}
