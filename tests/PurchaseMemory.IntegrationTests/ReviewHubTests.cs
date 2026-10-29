using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PurchaseMemory.Application.Purchases;
using PurchaseMemory.Application.ReviewQueue;
using PurchaseMemory.Infrastructure.Persistence;

namespace PurchaseMemory.IntegrationTests;

/// <summary>
/// The «Ревью» hub over <c>GET /api/review-queue</c> and <c>/count</c>: the
/// hub's own kinds (unchecked purchase lines, the caller's lines to rate),
/// the summary block (total = sum of sections, never filtered), the caller's
/// ratings kept personal, and access.
/// </summary>
public sealed class ReviewHubTests
{
    private const string AnnaId = "11111111-1111-1111-1111-111111111111";
    private const string BorisId = "22222222-2222-2222-2222-222222222222";

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static string Anna => AuthTestHost.Token(subject: AnnaId, email: "anna@example.com");

    private static string Boris => AuthTestHost.Token(subject: BorisId, email: "boris@example.com");

    [Fact]
    public async Task APurchaseWithUncheckedLinesIsOneItemOpenedOnItsFirstUncheckedLine()
    {
        await using var host = new AuthTestHost();
        var purchase = await SaveAsync(host.Services, "Lidl", DateTimeOffset.UtcNow.AddDays(-1), ("CHLEB", 4.00m), ("MASLO", 7.49m), ("RABAT", -1.00m));
        using var anna = host.CreateClientFor(Anna);
        await ApproveAsync(anna, purchase.PurchaseId, purchase.LineIds[0]);

        var queue = await anna.GetFromJsonAsync<JsonElement>("/api/review-queue", Cancel);

        var item = Assert.Single(Group(queue, "unapprovedLines").GetProperty("items").EnumerateArray());
        Assert.Equal($"unapprovedLines:{purchase.PurchaseId:D}", item.GetProperty("id").GetString());
        Assert.Equal(purchase.PurchaseId, item.GetProperty("purchaseId").GetGuid());
        Assert.Equal("Lidl", item.GetProperty("title").GetString());
        // The discount is a line of the review too.
        Assert.Equal(2, item.GetProperty("affectedLineCount").GetInt32());
        Assert.Equal(3, item.GetProperty("lineCount").GetInt32());
        Assert.Equal(1, item.GetProperty("itemPosition").GetInt32());

        await ApproveAsync(anna, purchase.PurchaseId, purchase.LineIds[1], purchase.LineIds[2]);
        var after = await anna.GetFromJsonAsync<JsonElement>("/api/review-queue", Cancel);
        Assert.False(TryGroup(after, "unapprovedLines", out _));
    }

    [Fact]
    public async Task PurchasesSavedBeforeTheLastThirtyDaysDoNotWaitForALineCheck()
    {
        await using var host = new AuthTestHost();
        var recent = await SaveAsync(host.Services, "Lidl", DateTimeOffset.UtcNow.AddDays(-1), ("CHLEB", 4.00m));
        var old = await SaveAsync(host.Services, "Biedronka", DateTimeOffset.UtcNow.AddDays(-40), ("MLEKO", 3.49m));
        await using (var scope = host.Services.CreateAsyncScope())
        {
            // Saved before line approvals existed: only its save date matters, not the receipt date.
            var dbContext = scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>();
            await dbContext.PurchaseDrafts
                .Where(purchase => purchase.Id == old.PurchaseId)
                .ExecuteUpdateAsync(update => update.SetProperty(purchase => purchase.CreatedAt, DateTimeOffset.UtcNow.AddDays(-31)), Cancel);
        }

        using var anna = host.CreateClientFor(Anna);
        var queue = await anna.GetFromJsonAsync<JsonElement>("/api/review-queue", Cancel);
        var count = await anna.GetFromJsonAsync<JsonElement>("/api/review-queue/count", Cancel);

        var item = Assert.Single(Group(queue, "unapprovedLines").GetProperty("items").EnumerateArray());
        Assert.Equal(recent.PurchaseId, item.GetProperty("purchaseId").GetGuid());
        Assert.Equal(1, Section(queue, "purchases"));
        Assert.Equal(1, count.GetProperty("byKind").GetProperty("unapprovedLines").GetInt32());
        Assert.Equal(queue.GetProperty("summary").GetProperty("totalCount").GetInt32(), count.GetProperty("totalCount").GetInt32());
        Assert.Equal(queue.GetProperty("totalCount").GetInt32(), count.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task LinesToRateArePersonalAndLeaveOnceRated()
    {
        await using var host = new AuthTestHost();
        var purchase = await SaveAsync(host.Services, "Biedronka", DateTimeOffset.UtcNow.AddDays(-2), ("SER", 6.99m), ("JAJA", 9.99m));
        using var anna = host.CreateClientFor(Anna);
        using var boris = host.CreateClientFor(Boris);
        var ser = purchase.LineIds[0];
        Assert.Equal(HttpStatusCode.Created, (await RateAsync(anna, ser, "like")).StatusCode);

        var annaQueue = await anna.GetFromJsonAsync<JsonElement>("/api/review-queue", Cancel);
        var annaItem = Assert.Single(Group(annaQueue, "unratedPurchase").GetProperty("items").EnumerateArray());
        Assert.Equal(purchase.LineIds[1], annaItem.GetProperty("purchaseItemId").GetGuid());
        Assert.Equal("JAJA", annaItem.GetProperty("title").GetString());
        Assert.Equal("Biedronka", annaItem.GetProperty("merchantName").GetString());
        Assert.Equal(9.99m, annaItem.GetProperty("price").GetDecimal());
        Assert.Equal(purchase.PurchaseId, annaItem.GetProperty("purchaseId").GetGuid());

        // Boris has rated nothing: Anna's rating does not take the line off his list.
        var borisQueue = await boris.GetFromJsonAsync<JsonElement>("/api/review-queue", Cancel);
        Assert.Equal(2, Group(borisQueue, "unratedPurchase").GetProperty("totalCount").GetInt32());
        Assert.Equal(1, Section(annaQueue, "ratings"));
        Assert.Equal(2, Section(borisQueue, "ratings"));

        var annaCount = await anna.GetFromJsonAsync<JsonElement>("/api/review-queue/count", Cancel);
        var borisCount = await boris.GetFromJsonAsync<JsonElement>("/api/review-queue/count", Cancel);
        Assert.Equal(1, annaCount.GetProperty("byKind").GetProperty("unratedPurchase").GetInt32());
        Assert.Equal(borisCount.GetProperty("totalCount").GetInt32() - 1, annaCount.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task TheSummaryAddsUpTheSectionsAndAgreesWithTheListAndTheCount()
    {
        await using var host = new AuthTestHost();
        // Line totals 15.00 against a receipt total of 20.00: «Сумма не сходится»; new products: «Без категории».
        await SaveAsync(host.Services, "Żabka", DateTimeOffset.UtcNow.AddDays(-1), total: 20.00m, ("KAWA", 15.00m));
        await SaveAsync(host.Services, "Lidl", DateTimeOffset.UtcNow.AddDays(-3), ("MLEKO", 3.49m));
        using var anna = host.CreateClientFor(Anna);

        var queue = await anna.GetFromJsonAsync<JsonElement>("/api/review-queue", Cancel);
        var count = await anna.GetFromJsonAsync<JsonElement>("/api/review-queue/count", Cancel);

        var summary = queue.GetProperty("summary");
        var sections = summary.GetProperty("sections").EnumerateArray().ToList();
        Assert.Equal(["receipts", "purchases", "products", "categories", "ratings"], sections.Select(section => section.GetProperty("section").GetString()));
        Assert.Equal(sections.Sum(section => section.GetProperty("count").GetInt32()), summary.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, Section(queue, "receipts"));
        Assert.Equal(2, Section(queue, "purchases"));
        Assert.Equal(0, Section(queue, "products"));
        Assert.Equal(2, Section(queue, "categories"));
        Assert.Equal(2, Section(queue, "ratings"));
        Assert.Equal(7, summary.GetProperty("totalCount").GetInt32());
        // Unfiltered, the list, the summary and the badge all say the same.
        Assert.Equal(7, queue.GetProperty("totalCount").GetInt32());
        Assert.Equal(7, count.GetProperty("totalCount").GetInt32());
        Assert.Equal(7, count.GetProperty("summary").GetProperty("totalCount").GetInt32());
        Assert.Equal(7, queue.GetProperty("groups").EnumerateArray().Sum(group => group.GetProperty("totalCount").GetInt32()));
    }

    [Fact]
    public async Task AFilterNarrowsTheListButNotTheSummary()
    {
        await using var host = new AuthTestHost();
        var lidl = await SaveAsync(host.Services, "Lidl", DateTimeOffset.UtcNow.AddDays(-1), ("MLEKO", 3.49m));
        await SaveAsync(host.Services, "Biedronka", DateTimeOffset.UtcNow.AddDays(-2), ("SOK", 5.99m));
        await using (var scope = host.Services.CreateAsyncScope())
        {
            // Give only MLEKO a confirmed category, so «без категории» matches SOK alone.
            var dbContext = scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>();
            var mleko = await dbContext.Products.SingleAsync(product => product.CanonicalName == "MLEKO", Cancel);
            await scope.ServiceProvider.GetRequiredService<Application.Products.Categories.IProductCategoryService>().ClassifyProductAsync(
                new Application.Products.Categories.ClassifyProductCommand(mleko.Id, true, Domain.Products.ProductCategorySeed.IdFor(2), false, null),
                Application.Receipts.ReceiptActor.Unattributed,
                Cancel);
        }

        using var anna = host.CreateClientFor(Anna);
        var whole = await anna.GetFromJsonAsync<JsonElement>("/api/review-queue", Cancel);
        var filtered = await anna.GetFromJsonAsync<JsonElement>("/api/review-queue?uncategorized=true", Cancel);

        Assert.Equal(whole.GetProperty("summary").GetRawText(), filtered.GetProperty("summary").GetRawText());
        Assert.True(filtered.GetProperty("totalCount").GetInt32() < whole.GetProperty("totalCount").GetInt32());
        // The Lidl purchase (MLEKO, categorized) is filtered out of every kind.
        Assert.DoesNotContain(
            filtered.GetProperty("groups").EnumerateArray().SelectMany(group => group.GetProperty("items").EnumerateArray()),
            item => item.GetProperty("purchaseId").ValueKind == JsonValueKind.String && item.GetProperty("purchaseId").GetGuid() == lidl.PurchaseId);
        Assert.Equal(1, Group(filtered, "unapprovedLines").GetProperty("totalCount").GetInt32());
        Assert.Equal(1, Group(filtered, "unratedPurchase").GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task TheDataQueueWithoutAViewerKeepsItsOwnKindsOnly()
    {
        await using var host = new AuthTestHost();
        await SaveAsync(host.Services, "Lidl", DateTimeOffset.UtcNow.AddDays(-1), ("MLEKO", 3.49m));

        await using var scope = host.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IReviewQueueService>();
        var queue = await service.GetAsync(20, Cancel);
        var hub = await service.GetHubAsync(20, ReviewQueueFilter.None, AnnaId, Cancel);

        Assert.Null(queue.Summary);
        Assert.Equal([ReviewQueueItemKind.MissingCategory], queue.Groups.Select(group => group.Kind));
        Assert.Equal(
            [ReviewQueueItemKind.MissingCategory, ReviewQueueItemKind.UnapprovedLines, ReviewQueueItemKind.UnratedPurchase],
            hub.Groups.Select(group => group.Kind));
        Assert.Equal(hub.TotalCount, hub.Summary!.TotalCount);
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetHubAsync(20, ReviewQueueFilter.None, " ", Cancel));
    }

    [Fact]
    public async Task TheHubNeedsAnApprovedUser()
    {
        await using var host = new AuthTestHost();
        var callers = new (string? Token, HttpStatusCode Expected)[]
        {
            (null, HttpStatusCode.Unauthorized),
            (AuthTestHost.Token(key: AuthTestHost.ForeignKey), HttpStatusCode.Unauthorized),
            (AuthTestHost.Token(access: "pending", subject: BorisId), HttpStatusCode.Forbidden),
            (AuthTestHost.Token(access: "blocked", subject: BorisId), HttpStatusCode.Forbidden),
            (Anna, HttpStatusCode.OK)
        };
        foreach (var (token, expected) in callers)
        {
            using var client = host.CreateClientFor(token);
            Assert.Equal(expected, (await client.GetAsync("/api/review-queue", Cancel)).StatusCode);
            Assert.Equal(expected, (await client.GetAsync("/api/review-queue/count", Cancel)).StatusCode);
        }
    }

    private static JsonElement Group(JsonElement queue, string kind) =>
        TryGroup(queue, kind, out var group) ? group : throw new Xunit.Sdk.XunitException($"No group {kind}.");

    private static bool TryGroup(JsonElement queue, string kind, out JsonElement group)
    {
        foreach (var candidate in queue.GetProperty("groups").EnumerateArray())
        {
            if (candidate.GetProperty("kind").GetString() == kind)
            {
                group = candidate;
                return true;
            }
        }

        group = default;
        return false;
    }

    private static int Section(JsonElement queue, string section) =>
        queue.GetProperty("summary").GetProperty("sections").EnumerateArray()
            .Single(entry => entry.GetProperty("section").GetString() == section)
            .GetProperty("count").GetInt32();

    private static async Task ApproveAsync(HttpClient client, Guid purchaseId, params Guid[] lineIds)
    {
        var items = new JsonArray();
        foreach (var lineId in lineIds)
        {
            items.Add(new JsonObject { ["purchaseId"] = purchaseId, ["lineId"] = lineId, ["approved"] = true, ["at"] = null });
        }

        var response = await client.PostAsJsonAsync("/api/purchases/line-approvals", new JsonObject { ["items"] = items }, Cancel);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static Task<HttpResponseMessage> RateAsync(HttpClient client, Guid purchaseItemId, string rating) =>
        client.PostAsync(
            "/api/impressions",
            new MultipartFormDataContent
            {
                { new StringContent(purchaseItemId.ToString()), "purchaseItemId" },
                { new StringContent(rating), "rating" }
            },
            Cancel);

    private static Task<SavedPurchase> SaveAsync(
        IServiceProvider services,
        string merchant,
        DateTimeOffset purchasedAt,
        params (string Name, decimal Price)[] lines) =>
        SaveAsync(services, merchant, purchasedAt, lines.Sum(line => line.Price), lines);

    private static async Task<SavedPurchase> SaveAsync(
        IServiceProvider services,
        string merchant,
        DateTimeOffset purchasedAt,
        decimal total,
        params (string Name, decimal Price)[] lines)
    {
        await using var scope = services.CreateAsyncScope();
        var view = await scope.ServiceProvider.GetRequiredService<IPurchaseDraftService>().CreateAsync(
            new CreatePurchaseDraftCommand(
                merchant,
                purchasedAt,
                "PLN",
                total,
                lines.Select(line => line.Price < 0
                    ? new PurchaseItemCommand(line.Name, null, null, null, line.Price, Domain.Receipts.ReceiptLineKind.Discount)
                    : new PurchaseItemCommand(line.Name, 1, "szt", line.Price, line.Price)).ToList(),
                AllowPossibleDuplicate: true),
            Cancel);

        var dbContext = scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>();
        var lineIds = await dbContext.Set<Domain.Purchases.PurchaseItem>()
            .AsNoTracking()
            .Where(item => EF.Property<Guid>(item, "PurchaseDraftId") == view.PurchaseId)
            .OrderBy(item => item.Position)
            .Select(item => item.Id)
            .ToListAsync(Cancel);
        return new SavedPurchase(view.PurchaseId, lineIds);
    }

    private sealed record SavedPurchase(Guid PurchaseId, IReadOnlyList<Guid> LineIds);
}
