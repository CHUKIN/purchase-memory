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
    public async Task AFlaggedLineCanBeRelinkedToAnExistingProductAndTheStoreRemembersIt()
    {
        await using var host = new AuthTestHost();
        await SaveAsync(host.Services, "Biedronka", null, ("Ser Gouda plastry 150g", 1, 5m, 5m));
        var second = await SaveAsync(host.Services, "Biedronka", null, ("Ser Gouda plastry 250g", 1, 7m, 7m));
        Guid existingId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>();
            existingId = (await dbContext.Products.SingleAsync(product => product.CanonicalName == "Ser Gouda plastry 150g", Cancel)).Id;
        }

        using var client = host.CreateClientFor(AuthTestHost.Token());

        // Every line has a product now; a doubtful one is its own product with the flag.
        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/purchases/{second.PurchaseId}", Cancel);
        var line = detail.GetProperty("items")[0];
        Assert.True(line.GetProperty("productNeedsReview").GetBoolean());
        Assert.Equal("Ser Gouda plastry 250g", line.GetProperty("productName").GetString());
        var flaggedId = line.GetProperty("productId").GetGuid();
        Assert.NotEqual(existingId, flaggedId);

        var body = Snapshot(detail);
        body["items"]![0]!["productLink"] = "existing";
        body["items"]![0]!["productId"] = existingId;
        var response = await client.PutAsJsonAsync($"/api/purchases/{second.PurchaseId}", body, Cancel);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        Assert.Equal(existingId, updated.GetProperty("items")[0].GetProperty("productId").GetGuid());
        Assert.False(updated.GetProperty("items")[0].GetProperty("productNeedsReview").GetBoolean());

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>();
            var alias = await dbContext.ProductAliases.SingleAsync(alias => alias.RawText == "Ser Gouda plastry 250g", Cancel);
            Assert.Equal(existingId, alias.ProductId);
            Assert.Equal(ProductAliasSource.HumanConfirmed, alias.Source);
        }

        // The next receipt from the same store follows the person's decision.
        var third = await SaveAsync(host.Services, "Biedronka", null, ("Ser Gouda plastry 250g", 1, 7m, 7m));
        var thirdDetail = await client.GetFromJsonAsync<JsonElement>($"/api/purchases/{third.PurchaseId}", Cancel);
        Assert.Equal(existingId, thirdDetail.GetProperty("items")[0].GetProperty("productId").GetGuid());
        Assert.False(thirdDetail.GetProperty("items")[0].GetProperty("productNeedsReview").GetBoolean());
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

        // «Создать товар» in the editor makes an ordinary product, never a flagged one.
        var line = updated.GetProperty("items")[0];
        Assert.Equal("Ser Gouda plastry 250g", line.GetProperty("productName").GetString());
        Assert.NotEqual(detail.GetProperty("items")[0].GetProperty("productId").GetGuid(), line.GetProperty("productId").GetGuid());
        Assert.False(line.GetProperty("productNeedsReview").GetBoolean());
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

    /// <summary>
    /// Regression: a line with an impression that keeps its position (an edited
    /// price or name) used to fail the save with an EF Core circular dependency.
    /// The impression and the «checked» mark follow the line through an edit, an
    /// inserted line before it and a reorder; nothing else on the lines changes.
    /// </summary>
    [Fact]
    public async Task ImpressionsAndApprovalsFollowTheirLineThroughEditsInsertsAndReorders()
    {
        await using var host = new AuthTestHost();
        var purchase = await SaveAsync(host.Services, "Lidl", 18.00m,
            ("MLEKO", 1, 3.00m, 3.00m), ("SER", 1, 6.00m, 6.00m), ("JAJA", 1, 9.00m, 9.00m));
        using var client = host.CreateClientFor(AuthTestHost.Token());
        var url = $"/api/purchases/{purchase.PurchaseId}";
        var detail = await client.GetFromJsonAsync<JsonElement>(url, Cancel);
        var serProductId = LineNamed(detail, "SER").GetProperty("productId").GetGuid();
        var like = await RateAsync(client, LineNamed(detail, "SER").GetProperty("id").GetGuid(), "like", "Вкусный");
        var dislike = await RateAsync(client, LineNamed(detail, "MLEKO").GetProperty("id").GetGuid(), "dislike", null);
        await ApproveAsync(client, purchase.PurchaseId, LineNamed(detail, "SER").GetProperty("id").GetGuid());
        await ApproveAsync(client, purchase.PurchaseId, LineNamed(detail, "MLEKO").GetProperty("id").GetGuid());
        detail = await client.GetFromJsonAsync<JsonElement>(url, Cancel);
        var serApprovedAt = LineNamed(detail, "SER").GetProperty("approvedAt").GetDateTimeOffset();
        var mlekoApprovedAt = LineNamed(detail, "MLEKO").GetProperty("approvedAt").GetDateTimeOffset();

        // 1. Same positions: SER is re-priced and renamed, MLEKO and JAJA are sent back unchanged.
        var edited = await PutAsync(client, url, EditorBody(detail, items =>
        {
            items[1]!["name"] = "SER GOUDA";
            items[1]!["unitPrice"] = 6.50m;
            items[1]!["lineTotal"] = 6.50m;
        }, receiptTotal: 18.50m));
        Assert.Equal(["MLEKO", "SER GOUDA", "JAJA"], Names(edited));
        Assert.Equal([3.00m, 6.50m, 9.00m], LineTotals(edited));
        Assert.Equal(serProductId, edited.GetProperty("items")[1].GetProperty("productId").GetGuid());
        await AssertFollowsAsync(client, purchase.PurchaseId, edited, like, "SER GOUDA", "Вкусный");
        await AssertFollowsAsync(client, purchase.PurchaseId, edited, dislike, "MLEKO", null);
        AssertApproved(edited, "SER GOUDA", serApprovedAt);
        AssertApproved(edited, "MLEKO", mlekoApprovedAt);
        Assert.False(LineNamed(edited, "JAJA").GetProperty("approved").GetBoolean());

        // 2. A new line before them shifts every position by one.
        var inserted = await PutAsync(client, url, EditorBody(edited, items => items.Insert(0, new JsonObject
        {
            ["name"] = "CHLEB", ["quantity"] = 1m, ["unit"] = "szt", ["unitPrice"] = 4.00m, ["lineTotal"] = 4.00m, ["productLink"] = "auto"
        }), receiptTotal: 22.50m));
        Assert.Equal(["CHLEB", "MLEKO", "SER GOUDA", "JAJA"], Names(inserted));
        Assert.Equal([4.00m, 3.00m, 6.50m, 9.00m], LineTotals(inserted));
        await AssertFollowsAsync(client, purchase.PurchaseId, inserted, like, "SER GOUDA", "Вкусный");
        await AssertFollowsAsync(client, purchase.PurchaseId, inserted, dislike, "MLEKO", null);
        AssertApproved(inserted, "SER GOUDA", serApprovedAt);
        AssertApproved(inserted, "MLEKO", mlekoApprovedAt);
        Assert.False(LineNamed(inserted, "CHLEB").GetProperty("approved").GetBoolean());

        // 3. Swapping the two rated lines swaps their positions, not their impressions.
        var reordered = await PutAsync(client, url, EditorBody(inserted, items =>
        {
            var mleko = items[1]!;
            items.RemoveAt(1);
            items.Insert(2, mleko);
        }, receiptTotal: 22.50m));
        Assert.Equal(["CHLEB", "SER GOUDA", "MLEKO", "JAJA"], Names(reordered));
        Assert.Equal([4.00m, 6.50m, 3.00m, 9.00m], LineTotals(reordered));
        await AssertFollowsAsync(client, purchase.PurchaseId, reordered, like, "SER GOUDA", "Вкусный");
        await AssertFollowsAsync(client, purchase.PurchaseId, reordered, dislike, "MLEKO", null);
        AssertApproved(reordered, "SER GOUDA", serApprovedAt);
        AssertApproved(reordered, "MLEKO", mlekoApprovedAt);

        var all = await ImpressionsAsync(client, purchase.PurchaseId);
        Assert.Equal(2, all.Count);
        Assert.Single((await client.GetFromJsonAsync<JsonElement>($"/api/products/{serProductId}/impressions", Cancel)).EnumerateArray());
    }

    /// <summary>
    /// Deleting a rated line keeps the impression (the current, intended
    /// behaviour): it stays on the purchase and its product, without a line.
    /// The other lines keep their impressions and «checked» marks.
    /// </summary>
    [Fact]
    public async Task DeletingARatedLineKeepsItsImpressionOnThePurchaseWithoutALine()
    {
        await using var host = new AuthTestHost();
        var purchase = await SaveAsync(host.Services, "Lidl", 18.00m,
            ("MLEKO", 1, 3.00m, 3.00m), ("SER", 1, 6.00m, 6.00m), ("JAJA", 1, 9.00m, 9.00m));
        using var client = host.CreateClientFor(AuthTestHost.Token());
        var url = $"/api/purchases/{purchase.PurchaseId}";
        var detail = await client.GetFromJsonAsync<JsonElement>(url, Cancel);
        var serProductId = LineNamed(detail, "SER").GetProperty("productId").GetGuid();
        var like = await RateAsync(client, LineNamed(detail, "SER").GetProperty("id").GetGuid(), "like", "Вкусный");
        var dislike = await RateAsync(client, LineNamed(detail, "JAJA").GetProperty("id").GetGuid(), "dislike", "Мелкие");
        await ApproveAsync(client, purchase.PurchaseId, LineNamed(detail, "SER").GetProperty("id").GetGuid());
        await ApproveAsync(client, purchase.PurchaseId, LineNamed(detail, "JAJA").GetProperty("id").GetGuid());
        detail = await client.GetFromJsonAsync<JsonElement>(url, Cancel);
        var jajaApprovedAt = LineNamed(detail, "JAJA").GetProperty("approvedAt").GetDateTimeOffset();

        // SER goes; JAJA moves from position 2 to 1.
        var updated = await PutAsync(client, url, EditorBody(detail, items => items.RemoveAt(1), receiptTotal: 12.00m));

        Assert.Equal(["MLEKO", "JAJA"], Names(updated));
        Assert.Equal([3.00m, 9.00m], LineTotals(updated));
        await AssertFollowsAsync(client, purchase.PurchaseId, updated, dislike, "JAJA", "Мелкие");
        AssertApproved(updated, "JAJA", jajaApprovedAt);
        Assert.False(LineNamed(updated, "MLEKO").GetProperty("approved").GetBoolean());

        var removed = (await ImpressionsAsync(client, purchase.PurchaseId)).Single(entry => entry.GetProperty("id").GetGuid() == like);
        Assert.Equal(JsonValueKind.Null, removed.GetProperty("purchaseItemId").ValueKind);
        Assert.Equal("like", removed.GetProperty("rating").GetString());
        Assert.Equal("Вкусный", removed.GetProperty("note").GetString());
        Assert.Equal("SER", removed.GetProperty("itemName").GetString());
        var forProduct = await client.GetFromJsonAsync<JsonElement>($"/api/products/{serProductId}/impressions", Cancel);
        Assert.Equal(like, Assert.Single(forProduct.EnumerateArray()).GetProperty("id").GetGuid());

        // A later edit of the purchase does not trip over the line-less impression.
        var again = await PutAsync(client, url, EditorBody(updated, items => items[0]!["name"] = "MLEKO 2%", receiptTotal: 12.00m));
        Assert.Equal(["MLEKO 2%", "JAJA"], Names(again));
        await AssertFollowsAsync(client, purchase.PurchaseId, again, dislike, "JAJA", "Мелкие");
    }

    /// <summary>
    /// A body without line ids (an older client or a script) falls back to the
    /// same-text match, and an unchanged-position line still saves.
    /// </summary>
    [Fact]
    public async Task ABodyWithoutLineIdsKeepsImpressionsByTextAtTheSamePosition()
    {
        await using var host = new AuthTestHost();
        var purchase = await SaveAsync(host.Services, "Lidl", 9.00m, ("MLEKO", 1, 3.00m, 3.00m), ("SER", 1, 6.00m, 6.00m));
        using var client = host.CreateClientFor(AuthTestHost.Token());
        var url = $"/api/purchases/{purchase.PurchaseId}";
        var detail = await client.GetFromJsonAsync<JsonElement>(url, Cancel);
        var like = await RateAsync(client, LineNamed(detail, "SER").GetProperty("id").GetGuid(), "like", null);

        var updated = await PutAsync(client, url, Change(detail, body =>
        {
            body["items"]![1]!["unitPrice"] = 6.50m;
            body["items"]![1]!["lineTotal"] = 6.50m;
            body["receiptTotal"] = 9.50m;
        }));

        Assert.Equal([3.00m, 6.50m], LineTotals(updated));
        await AssertFollowsAsync(client, purchase.PurchaseId, updated, like, "SER", null);
    }

    private static async Task<JsonElement> PutAsync(HttpClient client, string url, JsonObject body)
    {
        var response = await client.PutAsJsonAsync(url, body, Cancel);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);
    }

    /// <summary>Like <see cref="Snapshot"/>, but each line also carries its id, as the app's editor sends it.</summary>
    private static JsonObject EditorBody(JsonElement detail, Action<JsonArray> changeItems, decimal receiptTotal)
    {
        var body = Snapshot(detail);
        var items = body["items"]!.AsArray();
        var lines = detail.GetProperty("items").EnumerateArray().ToList();
        for (var position = 0; position < lines.Count; position++)
        {
            items[position]!["id"] = lines[position].GetProperty("id").GetGuid();
        }

        changeItems(items);
        body["receiptTotal"] = receiptTotal;
        return body;
    }

    private static JsonElement LineNamed(JsonElement detail, string name) =>
        detail.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("name").GetString() == name);

    private static List<string?> Names(JsonElement detail) =>
        detail.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("name").GetString()).ToList();

    private static List<decimal> LineTotals(JsonElement detail) =>
        detail.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("lineTotal").GetDecimal()).ToList();

    private static void AssertApproved(JsonElement detail, string name, DateTimeOffset approvedAt)
    {
        var line = LineNamed(detail, name);
        Assert.True(line.GetProperty("approved").GetBoolean());
        Assert.Equal(approvedAt, line.GetProperty("approvedAt").GetDateTimeOffset());
    }

    /// <summary>The impression is on the current row of the named line, with its rating and note intact.</summary>
    private static async Task AssertFollowsAsync(
        HttpClient client,
        Guid purchaseId,
        JsonElement detail,
        Guid impressionId,
        string lineName,
        string? note)
    {
        var impression = (await ImpressionsAsync(client, purchaseId)).Single(entry => entry.GetProperty("id").GetGuid() == impressionId);
        Assert.Equal(LineNamed(detail, lineName).GetProperty("id").GetGuid(), impression.GetProperty("purchaseItemId").GetGuid());
        Assert.Equal(note, impression.GetProperty("note").GetString());
    }

    private static async Task<List<JsonElement>> ImpressionsAsync(HttpClient client, Guid purchaseId) =>
        (await client.GetFromJsonAsync<JsonElement>($"/api/purchases/{purchaseId}/impressions", Cancel)).EnumerateArray().ToList();

    private static async Task<Guid> RateAsync(HttpClient client, Guid purchaseItemId, string rating, string? note)
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent(purchaseItemId.ToString()), "purchaseItemId" },
            { new StringContent(rating), "rating" }
        };
        if (note is not null)
        {
            form.Add(new StringContent(note), "note");
        }

        var response = await client.PostAsync("/api/impressions", form, Cancel);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("id").GetGuid();
    }

    private static async Task ApproveAsync(HttpClient client, Guid purchaseId, Guid lineId)
    {
        var response = await client.PostAsJsonAsync("/api/purchases/line-approvals", new
        {
            items = new[] { new { purchaseId, lineId, approved = true } }
        }, Cancel);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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
