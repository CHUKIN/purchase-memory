using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PurchaseMemory.Application.Products.Categories;
using PurchaseMemory.Application.Products.Tags;
using PurchaseMemory.Application.Purchases;
using PurchaseMemory.Application.ReviewQueue;
using PurchaseMemory.Application.Receipts;
using PurchaseMemory.Domain.Products;
using PurchaseMemory.Domain.Receipts;
using PurchaseMemory.Infrastructure.Persistence;

namespace PurchaseMemory.IntegrationTests;

/// <summary>The «Нужно проверить» queue: each kind from real stored data, limits and access.</summary>
public sealed class ReviewQueueTests
{
    private static readonly DateTimeOffset July1 = new(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid Dairy = ProductCategorySeed.IdFor(1);
    private static readonly Guid Milk = ProductCategorySeed.IdFor(2);
    private static readonly Guid Cheese = ProductCategorySeed.IdFor(3);
    private static readonly Guid Juices = ProductCategorySeed.IdFor(36);

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EmptyDatabaseHasAnEmptyQueue()
    {
        await using var application = await TestApplication.CreateAsync();
        var queue = await GetQueueAsync(application.Services, 20);
        Assert.Equal(0, queue.TotalCount);
        Assert.Empty(queue.Groups);
    }

    [Fact]
    public async Task CleanPurchaseOnlyAsksForTheNewProductsCategory()
    {
        await using var application = await TestApplication.CreateAsync();
        await SaveAsync(application.Services, "Biedronka", 10.00m, ("MLEKO 2%", 1, 10.00m, 10.00m));

        var queue = await GetQueueAsync(application.Services, 20);

        var group = Assert.Single(queue.Groups);
        Assert.Equal(ReviewQueueItemKind.MissingCategory, group.Kind);
        Assert.Equal("MLEKO 2%", Assert.Single(group.Items).Title);
    }

    [Fact]
    public async Task TotalMismatchIsDetectedWithDiscountsAndTolerance()
    {
        await using var application = await TestApplication.CreateAsync();
        // 12.00 - 2.00 discount = 10.00 matches; 10.005 would be inside tolerance too.
        await SaveAsync(application.Services, "Lidl", 10.00m, ("SER", 1, 12.00m, 12.00m), ("RABAT SER", null, null, -2.00m));
        var mismatched = await SaveAsync(application.Services, "Żabka", 20.00m, ("KAWA", 1, 15.00m, 15.00m));

        var queue = await GetQueueAsync(application.Services, 20);

        var item = Assert.Single(Group(queue, ReviewQueueItemKind.TotalMismatch).Items);
        Assert.Equal(mismatched.PurchaseId, item.PurchaseId);
        Assert.Equal($"totalMismatch:{mismatched.PurchaseId:D}", item.Id);
        Assert.Equal("Żabka", item.Title);
        Assert.Equal(20.00m, item.ReceiptTotal);
        Assert.Equal(15.00m, item.LinesTotal);
        Assert.Equal("PLN", item.Currency);
    }

    [Fact]
    public async Task MissingPricesAreCountedPerPurchase()
    {
        await using var application = await TestApplication.CreateAsync();
        var purchase = await SaveAsync(
            application.Services,
            "Lidl",
            null,
            ("CHLEB", 1, null, null),
            ("MASŁO", 1, null, null),
            ("JAJA", 1, 9.99m, 9.99m));

        var queue = await GetQueueAsync(application.Services, 20);

        var item = Assert.Single(Group(queue, ReviewQueueItemKind.MissingPrice).Items);
        Assert.Equal(purchase.PurchaseId, item.PurchaseId);
        Assert.Equal(2, item.AffectedLineCount);
        Assert.Null(Find(queue, ReviewQueueItemKind.TotalMismatch));
    }

    [Fact]
    public async Task FailedAndUnsavedReceiptsAreListedButSavedAndQueuedAreNot()
    {
        await using var application = await TestApplication.CreateAsync();
        var failed = Receipt();
        failed.Queue(July1);
        failed.StartProcessing(July1);
        failed.FailProcessing("timeout", July1);

        var ready = Receipt();
        ready.Queue(July1);
        ready.StartProcessing(July1);
        ready.CompleteProcessing("Biedronka", July1, "PLN", 5m,
            [ReceiptCaptureItem.Create(0, "MLEKO", 1, "szt", 5m, 5m)], "test", null, July1);

        var queued = Receipt();
        queued.Queue(July1);

        await using (var scope = application.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>();
            dbContext.ReceiptCaptures.AddRange(failed, ready, queued);
            await dbContext.SaveChangesAsync(Cancel);
        }

        var queue = await GetQueueAsync(application.Services, 20);

        Assert.Equal(failed.Id, Assert.Single(Group(queue, ReviewQueueItemKind.FailedReceipt).Items).ReceiptId);
        var unsaved = Assert.Single(Group(queue, ReviewQueueItemKind.UnsavedReceipt).Items);
        Assert.Equal(ready.Id, unsaved.ReceiptId);
        Assert.Equal("Biedronka", unsaved.Title);
        Assert.Equal(2, queue.TotalCount);
    }

    [Fact]
    public async Task AFlaggedProductIsListedWithItsCandidates()
    {
        await using var application = await TestApplication.CreateAsync();
        await SaveAsync(application.Services, "Biedronka", null, ("Ser Gouda plastry 150g", 1, 5m, 5m));
        await SaveAsync(application.Services, "Biedronka", null, ("Ser Gouda plastry 250g", 1, 7m, 7m));

        var queue = await GetQueueAsync(application.Services, 20);

        var item = Assert.Single(Group(queue, ReviewQueueItemKind.ProductNeedsReview).Items);
        Assert.Equal("Ser Gouda plastry 250g", item.Title);
        Assert.Equal($"productNeedsReview:{item.ProductId:D}", item.Id);
        Assert.Equal(1, item.PurchaseCount);
        // Both products lack a category, but the flagged one is a single item
        // that carries the category step too, so it is not listed twice.
        Assert.True(item.NeedsCategory);
        var existing = Assert.Single(Group(queue, ReviewQueueItemKind.MissingCategory).Items);
        Assert.NotEqual(item.ProductId, existing.ProductId);
        Assert.Equal(2, queue.TotalCount);
        var candidate = Assert.Single(item.Candidates!);
        Assert.Equal(existing.ProductId, candidate.ProductId);
        Assert.Equal("Ser Gouda plastry 150g", candidate.Name);
        Assert.Equal(1, candidate.PurchaseCount);
        Assert.InRange(candidate.Score, 0.62, 0.86);
    }

    [Fact]
    public async Task AFlaggedProductWithASuggestedCategoryIsOneItemThatAsksToConfirmIt()
    {
        await using var application = await TestApplication.CreateAsync();
        var services = application.Services;
        await SaveAsync(services, "Biedronka", null, ("Ser Gouda plastry 150g", 1, 5m, 5m));
        await SaveAsync(services, "Biedronka", null, ("Ser Gouda plastry 250g", 1, 7m, 7m));
        await using (var scope = services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>();
            (await dbContext.Products.SingleAsync(product => product.CanonicalName == "Ser Gouda plastry 150g", Cancel))
                .ConfirmCategory(Cheese, July1);
            // Categorization runs on new products, so a flagged one soon has a suggestion.
            (await dbContext.Products.SingleAsync(product => product.CanonicalName == "Ser Gouda plastry 250g", Cancel))
                .ApplySuggestion(Cheese, null, null, July1);
            await dbContext.SaveChangesAsync(Cancel);
        }

        var queue = await GetQueueAsync(services, 20);

        var item = Assert.Single(Group(queue, ReviewQueueItemKind.ProductNeedsReview).Items);
        Assert.Equal("Ser Gouda plastry 250g", item.Title);
        Assert.False(item.NeedsCategory);
        Assert.Equal(Cheese, item.CategoryId);
        Assert.False(string.IsNullOrEmpty(item.CategoryName));
        Assert.Null(Find(queue, ReviewQueueItemKind.SuggestedCategory));
        Assert.Null(Find(queue, ReviewQueueItemKind.MissingCategory));
        Assert.Equal(1, queue.TotalCount);

        // «Это другой товар» settles the product; the unconfirmed category is then an item of its own.
        await using (var scope = services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>();
            var flagged = await dbContext.Products.SingleAsync(product => product.Id == item.ProductId, Cancel);
            flagged.ClearReviewFlag(July1);
            await dbContext.SaveChangesAsync(Cancel);
        }

        var after = await GetQueueAsync(services, 20);
        Assert.Null(Find(after, ReviewQueueItemKind.ProductNeedsReview));
        Assert.Equal(item.ProductId, Assert.Single(Group(after, ReviewQueueItemKind.SuggestedCategory).Items).ProductId);
        Assert.Equal(1, after.TotalCount);
    }

    [Fact]
    public async Task AFlaggedProductOffersAtMostThreeCandidatesBestFirst()
    {
        await using var application = await TestApplication.CreateAsync();
        var services = application.Services;
        await SaveAsync(services, "Biedronka", null, ("SER ZOLTY", 1, 5m, 5m));

        Guid[] productIds;
        await using (var scope = services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>();
            string[] names = ["Gouda 150g", "Gouda 250g", "Edam 150g", "Gouda 500g", "Usunięty"];
            var products = names
                .Select(name => Product.Create(name, name.ToLowerInvariant(), July1))
                .ToArray();
            products[0].ConfirmBrand("Hochland", "hochland", July1);
            products[0].ConfirmCategory(Cheese, July1);
            dbContext.Products.AddRange(products);
            var flagged = await dbContext.Products.SingleAsync(product => product.CanonicalName == "SER ZOLTY", Cancel);
            flagged.FlagForReview(ProductReviewReason.AmbiguousMatch, July1);
            // Scores out of order on purpose; the vanished product must be skipped.
            dbContext.ProductReviewCandidates.AddRange(
                ProductReviewCandidate.Create(flagged.Id, 2, products[1].Id, 0.70),
                ProductReviewCandidate.Create(flagged.Id, 0, products[0].Id, 0.90),
                ProductReviewCandidate.Create(flagged.Id, 4, products[3].Id, 0.50),
                ProductReviewCandidate.Create(flagged.Id, 1, products[4].Id, 0.85),
                ProductReviewCandidate.Create(flagged.Id, 3, products[2].Id, 0.60));
            await dbContext.SaveChangesAsync(Cancel);
            dbContext.Products.Remove(products[4]);
            await dbContext.SaveChangesAsync(Cancel);
            productIds = products.Select(product => product.Id).ToArray();
        }

        var item = Assert.Single(Group(await GetQueueAsync(services, 20), ReviewQueueItemKind.ProductNeedsReview).Items);

        Assert.Equal([productIds[0], productIds[1], productIds[2]], item.Candidates!.Select(candidate => candidate.ProductId));
        Assert.Equal(new ReviewQueueCandidate(productIds[0], "Gouda 150g", "Hochland", Cheese, 0, 0.90), item.Candidates![0]);
        Assert.Null(item.Candidates[1].Brand);
        Assert.Null(item.Candidates[1].CategoryId);
    }

    [Fact]
    public async Task SuggestedCategoryIsListedUntilConfirmed()
    {
        await using var application = await TestApplication.CreateAsync();
        await SaveAsync(application.Services, "Biedronka", null, ("MLEKO", 1, 3m, 3m), ("CHLEB", 1, 4m, 4m));

        Guid productId;
        await using (var scope = application.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>();
            var milk = await dbContext.Products.SingleAsync(product => product.CanonicalName == "MLEKO", Cancel);
            milk.ApplySuggestion(Milk, null, null, July1);
            var bread = await dbContext.Products.SingleAsync(product => product.CanonicalName == "CHLEB", Cancel);
            bread.ConfirmCategory(null, July1); // a deliberate «Без категории» is not asked again
            await dbContext.SaveChangesAsync(Cancel);
            productId = milk.Id;
        }

        var queue = await GetQueueAsync(application.Services, 20);
        var suggested = Assert.Single(Group(queue, ReviewQueueItemKind.SuggestedCategory).Items);
        Assert.Equal(productId, suggested.ProductId);
        Assert.Equal(Milk, suggested.CategoryId);
        Assert.False(string.IsNullOrEmpty(suggested.CategoryName));
        Assert.Null(Find(queue, ReviewQueueItemKind.MissingCategory));

        await using (var scope = application.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IProductCategoryService>().ClassifyProductAsync(
                new ClassifyProductCommand(productId, true, Milk, false, null),
                ReceiptActor.Unattributed,
                Cancel);
        }

        Assert.Equal(0, (await GetQueueAsync(application.Services, 20)).TotalCount);
    }

    [Fact]
    public async Task LimitCapsItemsButKeepsTheTotal()
    {
        await using var application = await TestApplication.CreateAsync();
        for (var index = 0; index < 3; index++)
        {
            await SaveAsync(application.Services, $"Shop {index}", 99m, ($"ITEM {index}", 1, 1m, 1m));
        }

        var queue = await GetQueueAsync(application.Services, 2);
        var group = Group(queue, ReviewQueueItemKind.TotalMismatch);
        Assert.Equal(3, group.TotalCount);
        Assert.Equal(2, group.Items.Count);
        Assert.Equal(6, queue.TotalCount);

        var countOnly = await GetQueueAsync(application.Services, 0);
        Assert.Equal(6, countOnly.TotalCount);
        Assert.All(countOnly.Groups, g => Assert.Empty(g.Items));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => GetQueueAsync(application.Services, 101));
    }

    [Theory]
    [InlineData("/api/review-queue")]
    [InlineData("/api/review-queue/count")]
    public async Task QueueNeedsAnApprovedValidToken(string path)
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
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path, Cancel)).StatusCode);
        }

        using var pending = host.CreateClientFor(AuthTestHost.Token(access: "pending"));
        Assert.Equal(HttpStatusCode.Forbidden, (await pending.GetAsync(path, Cancel)).StatusCode);
    }

    [Fact]
    public async Task ApprovedUserReadsTheQueueAndCountOverHttp()
    {
        await using var host = new AuthTestHost();
        await SaveAsync(host.Services, "Żabka", 20.00m, ("KAWA", 1, 15.00m, 15.00m));
        using var client = host.CreateClientFor(AuthTestHost.Token());

        var queue = await client.GetFromJsonAsync<JsonElement>("/api/review-queue?limitPerKind=5", Cancel);
        Assert.Equal(2, queue.GetProperty("totalCount").GetInt32());
        var kinds = queue.GetProperty("groups").EnumerateArray().Select(g => g.GetProperty("kind").GetString()).ToList();
        Assert.Equal(["totalMismatch", "missingCategory"], kinds);
        var item = queue.GetProperty("groups")[0].GetProperty("items")[0];
        Assert.Equal(15.00m, item.GetProperty("linesTotal").GetDecimal());

        var count = await client.GetFromJsonAsync<JsonElement>("/api/review-queue/count", Cancel);
        Assert.Equal(2, count.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, count.GetProperty("byKind").GetProperty("totalMismatch").GetInt32());

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/review-queue?limitPerKind=0", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/review-queue?limitPerKind=101", Cancel)).StatusCode);
    }

    [Fact]
    public async Task TagFilterKeepsOnlyItemsAboutTaggedProducts()
    {
        await using var application = await TestApplication.CreateAsync();
        var services = application.Services;
        var tagged = await SaveAsync(services, "Lidl", 99m, ("SOK JABLKOWY", 1, 6m, 6m), ("CHLEB", 1, 4m, 4m));
        var untagged = await SaveAsync(services, "Żabka", 20m, ("KAWA", 1, 15m, 15m));
        var missingPrice = await SaveAsync(services, "Biedronka", null, ("MASŁO", 1, null, null));
        await SaveAsync(services, "Biedronka", null, ("Ser Gouda plastry 150g", 1, 5m, 5m));
        await SaveAsync(services, "Biedronka", null, ("Ser Gouda plastry 250g", 1, 7m, 7m));
        await AddFailedReceiptAsync(services);

        var kid = await CreateTagAsync(services, "Для ребёнка");
        // The flagged 250g product's candidate is tagged, yet the flagged product itself is not.
        await TagAsync(services, kid, "SOK JABLKOWY", "MASŁO", "Ser Gouda plastry 150g");

        var all = await GetQueueAsync(services, 20);
        var filtered = await GetQueueAsync(services, 20, kid);

        Assert.Equal(
            [
                (ReviewQueueItemKind.TotalMismatch, "Lidl"),
                (ReviewQueueItemKind.MissingPrice, "Biedronka"),
                (ReviewQueueItemKind.MissingCategory, "MASŁO"),
                (ReviewQueueItemKind.MissingCategory, "SOK JABLKOWY"),
                (ReviewQueueItemKind.MissingCategory, "Ser Gouda plastry 150g")
            ],
            filtered.Groups.SelectMany(group => group.Items)
                .Select(item => (item.Kind, item.Title))
                .OrderBy(pair => pair.Kind)
                .ThenBy(pair => pair.Title, StringComparer.Ordinal));
        Assert.Equal(tagged.PurchaseId, Assert.Single(Group(filtered, ReviewQueueItemKind.TotalMismatch).Items).PurchaseId);
        Assert.Equal(missingPrice.PurchaseId, Assert.Single(Group(filtered, ReviewQueueItemKind.MissingPrice).Items).PurchaseId);
        Assert.Null(Find(filtered, ReviewQueueItemKind.ProductNeedsReview));
        Assert.Null(Find(filtered, ReviewQueueItemKind.FailedReceipt));
        Assert.Equal(5, filtered.TotalCount);

        // Without the filter nothing changed: untagged purchases, the failed receipt and the flagged product are all there.
        Assert.Contains(Group(all, ReviewQueueItemKind.TotalMismatch).Items, item => item.PurchaseId == untagged.PurchaseId);
        Assert.Equal("Ser Gouda plastry 250g", Assert.Single(Group(all, ReviewQueueItemKind.ProductNeedsReview).Items).Title);
        Assert.Single(Group(all, ReviewQueueItemKind.FailedReceipt).Items);
        Assert.Equal(10, all.TotalCount);

        // The limit still caps items per kind while the totals stay the filtered ones.
        var limited = await GetQueueAsync(services, 1, kid);
        Assert.Equal(3, Group(limited, ReviewQueueItemKind.MissingCategory).TotalCount);
        Assert.Single(Group(limited, ReviewQueueItemKind.MissingCategory).Items);
        Assert.Equal(5, limited.TotalCount);
    }

    [Fact]
    public async Task SeveralTagsMustAllBeOnOneProduct()
    {
        await using var application = await TestApplication.CreateAsync();
        var services = application.Services;
        var both = await SaveAsync(services, "Lidl", 99m, ("SOK JABLKOWY", 1, 6m, 6m));
        // Each tag on a different line of one purchase is not «all tags on a product».
        var split = await SaveAsync(services, "Żabka", 99m, ("JOGURT", 1, 3m, 3m), ("CIASTKA", 1, 4m, 4m));
        await SaveAsync(services, "Aldi", 99m, ("KAWA", 1, 15m, 15m));

        var kid = await CreateTagAsync(services, "Для ребёнка");
        var promo = await CreateTagAsync(services, "Акция");
        await TagAsync(services, kid, "SOK JABLKOWY", "JOGURT");
        await TagAsync(services, promo, "SOK JABLKOWY", "CIASTKA");

        // Suggested categories are filtered by their product too.
        await using (var scope = services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>();
            var juice = await dbContext.Products.SingleAsync(product => product.CanonicalName == "SOK JABLKOWY", Cancel);
            juice.ApplySuggestion(Milk, null, null, July1);
            await dbContext.SaveChangesAsync(Cancel);
        }

        var queue = await GetQueueAsync(services, 20, kid, promo, kid);
        Assert.Equal(both.PurchaseId, Assert.Single(Group(queue, ReviewQueueItemKind.TotalMismatch).Items).PurchaseId);
        Assert.Equal("SOK JABLKOWY", Assert.Single(Group(queue, ReviewQueueItemKind.SuggestedCategory).Items).Title);
        Assert.Null(Find(queue, ReviewQueueItemKind.MissingCategory));
        Assert.Equal(2, queue.TotalCount);

        var promoOnly = await GetQueueAsync(services, 20, promo);
        Assert.Equal(
            new[] { both.PurchaseId, split.PurchaseId }.Order(),
            Group(promoOnly, ReviewQueueItemKind.TotalMismatch).Items.Select(item => item.PurchaseId!.Value).Order());
        Assert.Equal("CIASTKA", Assert.Single(Group(promoOnly, ReviewQueueItemKind.MissingCategory).Items).Title);
    }

    [Fact]
    public async Task TagFilterValidatesLikeTheFeed()
    {
        await using var application = await TestApplication.CreateAsync();
        var services = application.Services;
        await SaveAsync(services, "Lidl", 99m, ("SOK JABLKOWY", 1, 6m, 6m));
        var kid = await CreateTagAsync(services, "Для ребёнка");

        // A known tag nobody uses yet is a valid, empty filter.
        var unused = await GetQueueAsync(services, 20, kid);
        Assert.Equal(0, unused.TotalCount);
        Assert.Empty(unused.Groups);

        var unknown = Guid.NewGuid();
        var error = await Assert.ThrowsAsync<ProductTagNotFoundException>(() => GetQueueAsync(services, 20, kid, unknown));
        Assert.Equal(unknown, error.TagId);
        await Assert.ThrowsAsync<ProductTagNotFoundException>(() => GetQueueAsync(services, 20, Guid.Empty));

        // An empty list is no filter at all.
        Assert.Equal(2, (await GetQueueAsync(services, 20, [])).TotalCount);
    }

    [Fact]
    public async Task HttpFiltersTheListByTagButTheBadgeCountStaysWhole()
    {
        await using var host = new AuthTestHost();
        await SaveAsync(host.Services, "Lidl", 99m, ("SOK JABLKOWY", 1, 6m, 6m));
        await SaveAsync(host.Services, "Żabka", 20m, ("KAWA", 1, 15m, 15m));
        var kid = await CreateTagAsync(host.Services, "Для ребёнка");
        var promo = await CreateTagAsync(host.Services, "Акция");
        await TagAsync(host.Services, kid, "SOK JABLKOWY");
        await TagAsync(host.Services, promo, "SOK JABLKOWY", "KAWA");
        using var client = host.CreateClientFor(AuthTestHost.Token());

        var filtered = await client.GetFromJsonAsync<JsonElement>($"/api/review-queue?limitPerKind=5&tagId={kid}", Cancel);
        Assert.Equal(2, filtered.GetProperty("totalCount").GetInt32());
        var titles = filtered.GetProperty("groups").EnumerateArray()
            .SelectMany(group => group.GetProperty("items").EnumerateArray())
            .Select(item => item.GetProperty("title").GetString())
            .ToList();
        Assert.Equal(["Lidl", "SOK JABLKOWY"], titles);

        var both = await client.GetFromJsonAsync<JsonElement>($"/api/review-queue?tagId={promo}&tagId={kid}", Cancel);
        Assert.Equal(2, both.GetProperty("totalCount").GetInt32());
        var promoOnly = await client.GetFromJsonAsync<JsonElement>($"/api/review-queue?tagId={promo}", Cancel);
        Assert.Equal(4, promoOnly.GetProperty("totalCount").GetInt32());
        var blank = await client.GetFromJsonAsync<JsonElement>("/api/review-queue?tagId=", Cancel);
        Assert.Equal(4, blank.GetProperty("totalCount").GetInt32());

        // The navigation badge counts the whole queue whatever the screen shows.
        var count = await client.GetFromJsonAsync<JsonElement>($"/api/review-queue/count?tagId={kid}", Cancel);
        Assert.Equal(4, count.GetProperty("totalCount").GetInt32());

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/review-queue?tagId=детское", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/review-queue?tagId={Guid.NewGuid()}", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/review-queue?tagId={Guid.Empty}", Cancel)).StatusCode);
    }

    [Fact]
    public async Task TagFilterNeedsAnApprovedValidToken()
    {
        await using var host = new AuthTestHost();
        var kid = await CreateTagAsync(host.Services, "Для ребёнка");
        var path = $"/api/review-queue?tagId={kid}";
        foreach (var token in new[] { null, AuthTestHost.Token(key: AuthTestHost.ForeignKey) })
        {
            using var client = host.CreateClientFor(token);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path, Cancel)).StatusCode);
        }

        using var pending = host.CreateClientFor(AuthTestHost.Token(access: "pending"));
        Assert.Equal(HttpStatusCode.Forbidden, (await pending.GetAsync(path, Cancel)).StatusCode);
    }

    [Fact]
    public async Task McpListsTheQueueByTag()
    {
        await using var host = new AuthTestHost();
        await SaveAsync(host.Services, "Lidl", 99m, ("SOK JABLKOWY", 1, 6m, 6m));
        await SaveAsync(host.Services, "Żabka", 20m, ("KAWA", 1, 15m, 15m));
        var kid = await CreateTagAsync(host.Services, "Для ребёнка");
        await TagAsync(host.Services, kid, "SOK JABLKOWY");
        using var client = host.CreateClientFor(AuthTestHost.Token());

        var list = await (await PostMcpAsync(client, """{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}""")).Content.ReadAsStringAsync(Cancel);
        Assert.Contains("tagIds (ids from list_tags", list, StringComparison.Ordinal);

        var call = await PostMcpAsync(client,
            """{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"list_review_queue","arguments":{"tagIds":["KID"]}}}"""
                .Replace("KID", kid.ToString(), StringComparison.Ordinal));
        var body = await call.Content.ReadAsStringAsync(Cancel);
        Assert.Equal(HttpStatusCode.OK, call.StatusCode);
        Assert.Contains("SOK JABLKOWY", body, StringComparison.Ordinal);
        Assert.DoesNotContain("KAWA", body, StringComparison.Ordinal);

        var unknown = Guid.NewGuid();
        var failed = await (await PostMcpAsync(client,
            """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"list_review_queue","arguments":{"tagIds":["ID"]}}}"""
                .Replace("ID", unknown.ToString(), StringComparison.Ordinal))).Content.ReadAsStringAsync(Cancel);
        Assert.Contains("\"isError\":true", failed, StringComparison.Ordinal);
        Assert.Contains($"Tag {unknown} was not found.", failed, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CategoryFilterKeepsOnlyItemsAboutProductsInTheCategory()
    {
        await using var application = await TestApplication.CreateAsync();
        var services = application.Services;
        var lidl = await SaveAsync(services, "Lidl", 99m, ("SOK JABLKOWY", 1, 6m, 6m), ("CHLEB", 1, 4m, 4m));
        var zabka = await SaveAsync(services, "Żabka", 20m, ("KAWA", 1, 15m, 15m));
        var butter = await SaveAsync(services, "Biedronka", null, ("MASŁO", 1, null, null));
        await SaveAsync(services, "Biedronka", null, ("Ser Gouda plastry 150g", 1, 5m, 5m));
        await SaveAsync(services, "Biedronka", null, ("Ser Gouda plastry 250g", 1, 7m, 7m));
        await AddFailedReceiptAsync(services);

        await using (var scope = services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>();
            async Task<Product> FindProduct(string name) =>
                await dbContext.Products.SingleAsync(product => product.CanonicalName == name, Cancel);
            (await FindProduct("SOK JABLKOWY")).ConfirmCategory(Juices, July1);
            (await FindProduct("MASŁO")).ApplySuggestion(Milk, null, null, July1);
            // The flagged 250g product's only candidate is a cheese; the flagged product itself has no category.
            (await FindProduct("Ser Gouda plastry 150g")).ConfirmCategory(Cheese, July1);
            await dbContext.SaveChangesAsync(Cancel);
        }

        static List<(ReviewQueueItemKind, Guid?)> Keys(ReviewQueueResult queue) => queue.Groups
            .SelectMany(group => group.Items)
            .Select(item => (item.Kind, item.PurchaseId ?? item.ProductId))
            .OrderBy(pair => pair.Kind)
            .ThenBy(pair => pair.Item2)
            .ToList();

        // A group includes its subcategories; an AI-suggested category counts, as in the feed.
        var dairy = await GetQueueAsync(services, 20, new ReviewQueueFilter(CategoryId: Dairy));
        Assert.Equal(2, dairy.TotalCount);
        Assert.Equal(butter.PurchaseId, Assert.Single(Group(dairy, ReviewQueueItemKind.MissingPrice).Items).PurchaseId);
        Assert.Equal("MASŁO", Assert.Single(Group(dairy, ReviewQueueItemKind.SuggestedCategory).Items).Title);
        Assert.Equal(Keys(dairy), Keys(await GetQueueAsync(services, 20, new ReviewQueueFilter(CategoryId: Milk))));

        var cheese = await GetQueueAsync(services, 20, new ReviewQueueFilter(CategoryId: Cheese));
        Assert.Equal(0, cheese.TotalCount);

        var juices = await GetQueueAsync(services, 20, new ReviewQueueFilter(CategoryId: Juices));
        Assert.Equal(lidl.PurchaseId, Assert.Single(Assert.Single(juices.Groups).Items).PurchaseId);

        // «Без категории»: products nobody classified, and purchases with such a line.
        var none = await GetQueueAsync(services, 20, new ReviewQueueFilter(Uncategorized: true));
        Assert.Equal(
            new[] { lidl.PurchaseId, zabka.PurchaseId }.Order(),
            Group(none, ReviewQueueItemKind.TotalMismatch).Items.Select(item => item.PurchaseId!.Value).Order());
        Assert.Equal(
            ["CHLEB", "KAWA"],
            Group(none, ReviewQueueItemKind.MissingCategory).Items.Select(item => item.Title).Order(StringComparer.Ordinal));
        // The flagged product matches by itself (it has no category), never through its cheese
        // candidate, and is one item that also asks for the category.
        var flaggedWithoutCategory = Assert.Single(Group(none, ReviewQueueItemKind.ProductNeedsReview).Items);
        Assert.Equal("Ser Gouda plastry 250g", flaggedWithoutCategory.Title);
        Assert.True(flaggedWithoutCategory.NeedsCategory);
        Assert.Null(Find(cheese, ReviewQueueItemKind.ProductNeedsReview));
        Assert.Equal(5, none.TotalCount);
        Assert.Null(Find(none, ReviewQueueItemKind.FailedReceipt));

        // Category and tag combine (AND) on the same product.
        var kid = await CreateTagAsync(services, "Для ребёнка");
        await TagAsync(services, kid, "SOK JABLKOWY", "CHLEB");
        Assert.Equal(1, (await GetQueueAsync(services, 20, new ReviewQueueFilter(Juices, false, [kid]))).TotalCount);
        Assert.Equal(0, (await GetQueueAsync(services, 20, new ReviewQueueFilter(Dairy, false, [kid]))).TotalCount);
        Assert.Equal(2, (await GetQueueAsync(services, 20, new ReviewQueueFilter(null, true, [kid]))).TotalCount);

        // The whole queue is unchanged by any of this.
        Assert.Equal(8, (await GetQueueAsync(services, 20)).TotalCount);
    }

    [Fact]
    public async Task CategoryFilterValidatesLikeTheFeed()
    {
        await using var application = await TestApplication.CreateAsync();
        var services = application.Services;
        await SaveAsync(services, "Lidl", 99m, ("SOK JABLKOWY", 1, 6m, 6m));

        var unknown = Guid.NewGuid();
        var error = await Assert.ThrowsAsync<ProductCategoryNotFoundException>(
            () => GetQueueAsync(services, 20, new ReviewQueueFilter(CategoryId: unknown)));
        Assert.Contains(unknown.ToString(), error.Message, StringComparison.Ordinal);
        await Assert.ThrowsAsync<ArgumentException>(
            () => GetQueueAsync(services, 20, new ReviewQueueFilter(Milk, true)));

        // A known category nobody uses is a valid, empty filter.
        Assert.Equal(0, (await GetQueueAsync(services, 20, new ReviewQueueFilter(CategoryId: Juices))).TotalCount);
        Assert.Equal(2, (await GetQueueAsync(services, 20, ReviewQueueFilter.None)).TotalCount);
    }

    [Fact]
    public async Task HttpFiltersTheListByCategoryButTheBadgeCountStaysWhole()
    {
        await using var host = new AuthTestHost();
        await SaveAsync(host.Services, "Lidl", 99m, ("SOK JABLKOWY", 1, 6m, 6m));
        await SaveAsync(host.Services, "Żabka", 20m, ("KAWA", 1, 15m, 15m));
        await SaveAsync(host.Services, "Biedronka", null, ("Ser Gouda plastry 150g", 1, 5m, 5m));
        await SaveAsync(host.Services, "Biedronka", null, ("Ser Gouda plastry 250g", 1, 7m, 7m));
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>();
            (await dbContext.Products.SingleAsync(product => product.CanonicalName == "SOK JABLKOWY", Cancel))
                .ConfirmCategory(Juices, July1);
            await dbContext.SaveChangesAsync(Cancel);
        }

        using var client = host.CreateClientFor(AuthTestHost.Token());

        var juices = await client.GetFromJsonAsync<JsonElement>($"/api/review-queue?categoryId={Juices}", Cancel);
        Assert.Equal(1, juices.GetProperty("totalCount").GetInt32());
        var drinks = await client.GetFromJsonAsync<JsonElement>($"/api/review-queue?categoryId={ProductCategorySeed.IdFor(33)}", Cancel);
        Assert.Equal(1, drinks.GetProperty("totalCount").GetInt32());
        var none = await client.GetFromJsonAsync<JsonElement>("/api/review-queue?uncategorized=true", Cancel);
        // KAWA's mismatch and category, Gouda 150g's category, and the flagged 250g product once.
        Assert.Equal(4, none.GetProperty("totalCount").GetInt32());

        // The whole list carries the flagged product's candidates.
        var all = await client.GetFromJsonAsync<JsonElement>("/api/review-queue", Cancel);
        Assert.Equal(5, all.GetProperty("totalCount").GetInt32());
        var flagged = all.GetProperty("groups").EnumerateArray()
            .Single(group => group.GetProperty("kind").GetString() == "productNeedsReview")
            .GetProperty("items")[0];
        Assert.Equal("Ser Gouda plastry 250g", flagged.GetProperty("title").GetString());
        Assert.Equal(1, flagged.GetProperty("purchaseCount").GetInt32());
        Assert.True(flagged.GetProperty("needsCategory").GetBoolean());
        var candidate = Assert.Single(flagged.GetProperty("candidates").EnumerateArray());
        Assert.Equal("Ser Gouda plastry 150g", candidate.GetProperty("name").GetString());
        Assert.NotEqual(flagged.GetProperty("productId").GetGuid(), candidate.GetProperty("productId").GetGuid());
        Assert.Equal(1, candidate.GetProperty("purchaseCount").GetInt32());
        Assert.True(candidate.GetProperty("score").GetDouble() > 0);
        Assert.Equal(JsonValueKind.Null, candidate.GetProperty("brand").ValueKind);
        Assert.Equal(JsonValueKind.Null, candidate.GetProperty("categoryId").ValueKind);

        var count = await client.GetFromJsonAsync<JsonElement>($"/api/review-queue/count?categoryId={Juices}", Cancel);
        Assert.Equal(5, count.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, count.GetProperty("byKind").GetProperty("productNeedsReview").GetInt32());

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/review-queue?categoryId={Guid.NewGuid()}", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/review-queue?categoryId=молоко", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/review-queue?categoryId={Juices}&uncategorized=true", Cancel)).StatusCode);
    }

    [Fact]
    public async Task McpListsTheQueueByCategory()
    {
        await using var host = new AuthTestHost();
        await SaveAsync(host.Services, "Lidl", 99m, ("SOK JABLKOWY", 1, 6m, 6m));
        await SaveAsync(host.Services, "Kiosk", 20m, ("KAWA", 1, 15m, 15m));
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>();
            (await dbContext.Products.SingleAsync(product => product.CanonicalName == "SOK JABLKOWY", Cancel))
                .ConfirmCategory(Juices, July1);
            await dbContext.SaveChangesAsync(Cancel);
        }

        using var client = host.CreateClientFor(AuthTestHost.Token());

        var list = await (await PostMcpAsync(client, """{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}""")).Content.ReadAsStringAsync(Cancel);
        Assert.Contains("categoryId (from list_product_categories", list, StringComparison.Ordinal);

        async Task<JsonElement> CallAsync(string arguments)
        {
            var response = await PostMcpAsync(client,
                $$$"""{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"list_review_queue","arguments":{{{arguments}}}}}""");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            // Streamable HTTP answers as server-sent events: the JSON-RPC message is the `data:` line.
            var body = await response.Content.ReadAsStringAsync(Cancel);
            var message = body.Split('\n').Select(line => line.Trim()).LastOrDefault(line => line.StartsWith("data:", StringComparison.Ordinal))?[5..] ?? body;
            return JsonDocument.Parse(message).RootElement.GetProperty("result").Clone();
        }

        static IReadOnlyList<string> Titles(JsonElement result) => result.GetProperty("structuredContent").GetProperty("groups")
            .EnumerateArray()
            .SelectMany(group => group.GetProperty("items").EnumerateArray())
            .Select(item => item.GetProperty("title").GetString()!)
            .Order(StringComparer.Ordinal)
            .ToList();

        // Drinks (the group) includes Juices; the uncategorized KAWA and its purchase drop out.
        var drinks = await CallAsync($$"""{"categoryId":"{{ProductCategorySeed.IdFor(33)}}"}""");
        Assert.Equal(["Lidl"], Titles(drinks));

        var uncategorized = await CallAsync("""{"uncategorized":true}""");
        Assert.Equal(["KAWA", "Kiosk"], Titles(uncategorized));

        Assert.Equal(["KAWA", "Kiosk", "Lidl"], Titles(await CallAsync("{}")));

        var unknown = Guid.NewGuid();
        var missing = await CallAsync($$"""{"categoryId":"{{unknown}}"}""");
        Assert.True(missing.GetProperty("isError").GetBoolean());
        Assert.Contains($"Category {unknown} was not found.", missing.GetRawText(), StringComparison.Ordinal);

        var both = await CallAsync($$"""{"categoryId":"{{Juices}}","uncategorized":true}""");
        Assert.True(both.GetProperty("isError").GetBoolean());
        Assert.Contains("Choose either a category", both.GetRawText(), StringComparison.Ordinal);
    }

    private static ReviewQueueGroup Group(ReviewQueueResult queue, ReviewQueueItemKind kind) =>
        Find(queue, kind) ?? throw new Xunit.Sdk.XunitException($"No group {kind}.");

    private static ReviewQueueGroup? Find(ReviewQueueResult queue, ReviewQueueItemKind kind) =>
        queue.Groups.SingleOrDefault(group => group.Kind == kind);

    private static async Task<ReviewQueueResult> GetQueueAsync(IServiceProvider services, int limit)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IReviewQueueService>().GetAsync(limit, Cancel);
    }

    private static async Task<ReviewQueueResult> GetQueueAsync(IServiceProvider services, int limit, params Guid[] tagIds)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IReviewQueueService>().GetAsync(limit, tagIds, Cancel);
    }

    private static async Task<ReviewQueueResult> GetQueueAsync(IServiceProvider services, int limit, ReviewQueueFilter filter)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IReviewQueueService>().GetAsync(limit, filter, Cancel);
    }

    private static async Task<Guid> CreateTagAsync(IServiceProvider services, string name)
    {
        await using var scope = services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<IProductTagService>().CreateAsync(name, Cancel)).Id;
    }

    /// <summary>Adds <paramref name="tagId"/> to each named product, keeping its other tags.</summary>
    private static async Task TagAsync(IServiceProvider services, Guid tagId, params string[] productNames)
    {
        foreach (var name in productNames)
        {
            await using var scope = services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>();
            var productId = (await dbContext.Products.SingleAsync(product => product.CanonicalName == name, Cancel)).Id;
            var current = await dbContext.Set<ProductTagLink>()
                .Where(link => link.ProductId == productId)
                .Select(link => link.TagId)
                .ToListAsync(Cancel);
            await scope.ServiceProvider.GetRequiredService<IProductTagService>().SetProductTagsAsync(
                productId, [.. current, tagId], ReceiptActor.Unattributed, Cancel);
        }
    }

    private static async Task AddFailedReceiptAsync(IServiceProvider services)
    {
        var failed = Receipt();
        failed.Queue(July1);
        failed.StartProcessing(July1);
        failed.FailProcessing("timeout", July1);
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>();
        dbContext.ReceiptCaptures.Add(failed);
        await dbContext.SaveChangesAsync(Cancel);
    }

    private static Task<HttpResponseMessage> PostMcpAsync(HttpClient client, string body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        return client.SendAsync(request, Cancel);
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

    private static ReceiptCapture Receipt() => ReceiptCapture.CreateUploaded(
        Guid.NewGuid(),
        [ReceiptImagePart.Create(0, $"receipts/{Guid.NewGuid():N}.jpg", "image/jpeg", 10, Convert.ToHexStringLower(Guid.NewGuid().ToByteArray()) + Convert.ToHexStringLower(Guid.NewGuid().ToByteArray()))],
        July1);
}
