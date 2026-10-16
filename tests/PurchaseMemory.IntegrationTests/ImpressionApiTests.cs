using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PurchaseMemory.Application.Impressions;
using PurchaseMemory.Application.Purchases;
using PurchaseMemory.Infrastructure.Persistence;

namespace PurchaseMemory.IntegrationTests;

/// <summary>
/// Product impressions over HTTP: creating, listing per product/line/purchase,
/// per-person ownership, access, photos, validation, the «rate recent» list,
/// and impressions surviving a purchase correction.
/// </summary>
public sealed class ImpressionApiTests
{
    private const string AnnaId = "11111111-1111-1111-1111-111111111111";
    private const string BorisId = "22222222-2222-2222-2222-222222222222";

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static string Anna => AuthTestHost.Token(subject: AnnaId, email: "anna@example.com");

    private static string Boris => AuthTestHost.Token(subject: BorisId, email: "boris@example.com");

    [Fact]
    public async Task EachPersonKeepsAHistoryThatIsComparedAcrossStores()
    {
        await using var host = new AuthTestHost();
        var lidl = await SaveAsync(host.Services, "Lidl", DateTimeOffset.UtcNow.AddDays(-10), ("MLEKO 2%", 3.49m));
        var biedronka = await SaveAsync(host.Services, "Biedronka", DateTimeOffset.UtcNow.AddDays(-2), ("MLEKO 2%", 2.99m));
        var productId = lidl.Items.Single().ProductId!.Value;
        Assert.Equal(productId, biedronka.Items.Single().ProductId);
        using var anna = host.CreateClientFor(Anna);
        using var boris = host.CreateClientFor(Boris);

        var first = await CreateAsync(anna, lidl.Items.Single().Id, "like", "  Вкусное  ");
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var created = await first.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        Assert.Equal("like", created.GetProperty("rating").GetString());
        Assert.Equal("Вкусное", created.GetProperty("note").GetString());
        Assert.True(created.GetProperty("isMine").GetBoolean());
        Assert.Equal(AnnaId, created.GetProperty("authorUserId").GetString());
        Assert.Equal("Lidl", created.GetProperty("merchantName").GetString());
        Assert.Equal(3.49m, created.GetProperty("unitPrice").GetDecimal());
        Assert.Equal(productId, created.GetProperty("productId").GetGuid());
        Assert.Equal(JsonValueKind.Null, created.GetProperty("photoUrl").ValueKind);

        // A changed opinion is a new entry; the old one is not overwritten.
        Assert.Equal(HttpStatusCode.Created, (await CreateAsync(anna, biedronka.Items.Single().Id, "neutral")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await CreateAsync(boris, biedronka.Items.Single().Id, "dislike", "Кислое")).StatusCode);

        var asBoris = await boris.GetFromJsonAsync<JsonElement>($"/api/products/{productId}/impressions", Cancel);
        var entries = asBoris.EnumerateArray().ToList();
        Assert.Equal(3, entries.Count);
        Assert.Equal(2, entries.Count(entry => entry.GetProperty("authorUserId").GetString() == AnnaId));
        Assert.All(entries, entry => Assert.Equal(
            entry.GetProperty("authorUserId").GetString() == BorisId,
            entry.GetProperty("isMine").GetBoolean()));
        Assert.Equal(["Biedronka", "Biedronka", "Lidl"], entries.Select(entry => entry.GetProperty("merchantName").GetString()).Order());

        var forLine = await anna.GetFromJsonAsync<JsonElement>($"/api/purchase-items/{biedronka.Items.Single().Id}/impressions", Cancel);
        Assert.Equal(2, forLine.GetArrayLength());
        var forPurchase = await anna.GetFromJsonAsync<JsonElement>($"/api/purchases/{lidl.PurchaseId}/impressions", Cancel);
        Assert.Equal("like", Assert.Single(forPurchase.EnumerateArray()).GetProperty("rating").GetString());

        // The product page offers its purchases to rate, newest first.
        var lines = await anna.GetFromJsonAsync<JsonElement>($"/api/products/{productId}/rateable-lines", Cancel);
        Assert.Equal(["Biedronka", "Lidl"], lines.EnumerateArray().Select(line => line.GetProperty("merchantName").GetString()));
        Assert.Equal(biedronka.Items.Single().Id, lines[0].GetProperty("purchaseItemId").GetGuid());

        // The purchase page exposes line ids, which is what the rating control posts.
        var detail = await anna.GetFromJsonAsync<JsonElement>($"/api/purchases/{lidl.PurchaseId}", Cancel);
        Assert.Equal(lidl.Items.Single().Id, detail.GetProperty("items")[0].GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task OnlyTheAuthorCanWithdrawAnImpression()
    {
        await using var host = new AuthTestHost();
        var purchase = await SaveAsync(host.Services, "Lidl", DateTimeOffset.UtcNow, ("SER", 7.99m));
        using var anna = host.CreateClientFor(Anna);
        using var boris = host.CreateClientFor(Boris);
        var created = await (await CreateAsync(anna, purchase.Items.Single().Id, "like")).Content.ReadFromJsonAsync<JsonElement>(Cancel);
        var impressionId = created.GetProperty("id").GetGuid();

        var foreign = await boris.DeleteAsync($"/api/impressions/{impressionId}", Cancel);
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        Assert.Single((await anna.GetFromJsonAsync<JsonElement>($"/api/purchases/{purchase.PurchaseId}/impressions", Cancel)).EnumerateArray());

        Assert.Equal(HttpStatusCode.NoContent, (await anna.DeleteAsync($"/api/impressions/{impressionId}", Cancel)).StatusCode);
        Assert.Empty((await anna.GetFromJsonAsync<JsonElement>($"/api/purchases/{purchase.PurchaseId}/impressions", Cancel)).EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await anna.DeleteAsync($"/api/impressions/{impressionId}", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await boris.DeleteAsync($"/api/impressions/{Guid.NewGuid()}", Cancel)).StatusCode);

        // Withdrawn, not destroyed: the row stays for history.
        await using var scope = host.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>()
            .ProductImpressions.SingleAsync(entity => entity.Id == impressionId, Cancel);
        Assert.NotNull(stored.DeletedAt);
        Assert.Equal(AnnaId, stored.AuthorUserId);
    }

    [Fact]
    public async Task EveryEndpointRequiresAnApprovedUser()
    {
        await using var host = new AuthTestHost();
        var purchase = await SaveAsync(host.Services, "Lidl", DateTimeOffset.UtcNow, ("SER", 7.99m));
        var itemId = purchase.Items.Single().Id;
        var productId = purchase.Items.Single().ProductId!.Value;
        using var anna = host.CreateClientFor(Anna);
        var impressionId = (await (await CreateAsync(anna, itemId, "like")).Content.ReadFromJsonAsync<JsonElement>(Cancel))
            .GetProperty("id").GetGuid();

        string[] reads =
        [
            $"/api/products/{productId}/impressions",
            $"/api/purchase-items/{itemId}/impressions",
            $"/api/purchases/{purchase.PurchaseId}/impressions",
            "/api/impressions/unrated",
            $"/api/products/{productId}/rateable-lines",
            $"/api/impressions/{impressionId}/photo"
        ];
        var callers = new (string? Token, HttpStatusCode Expected)[]
        {
            (null, HttpStatusCode.Unauthorized),
            (AuthTestHost.Token(key: AuthTestHost.ForeignKey), HttpStatusCode.Unauthorized),
            (AuthTestHost.Token(access: "pending", subject: BorisId), HttpStatusCode.Forbidden),
            (AuthTestHost.Token(access: "blocked", subject: BorisId), HttpStatusCode.Forbidden)
        };
        foreach (var (token, expected) in callers)
        {
            using var client = host.CreateClientFor(token);
            foreach (var url in reads)
            {
                Assert.Equal(expected, (await client.GetAsync(url, Cancel)).StatusCode);
            }

            Assert.Equal(expected, (await CreateAsync(client, itemId, "dislike")).StatusCode);
            Assert.Equal(expected, (await client.DeleteAsync($"/api/impressions/{impressionId}", Cancel)).StatusCode);
        }

        // None of the refused calls changed anything.
        var remaining = await anna.GetFromJsonAsync<JsonElement>($"/api/purchase-items/{itemId}/impressions", Cancel);
        Assert.Equal("like", Assert.Single(remaining.EnumerateArray()).GetProperty("rating").GetString());
    }

    [Fact]
    public async Task APhotoIsStoredAsAMetadataFreeCopyAndServedBack()
    {
        await using var host = new AuthTestHost();
        var purchase = await SaveAsync(host.Services, "Lidl", DateTimeOffset.UtcNow, ("CZEKOLADA", 5.49m));
        using var anna = host.CreateClientFor(Anna);
        var original = TestImages.WithExif(TestImages.Jpeg(3200, 1600), orientation: 1);

        var response = await CreateAsync(anna, purchase.Items.Single().Id, "like", "Внутри", ("photo.jpg", "image/jpeg", original));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        var photoUrl = created.GetProperty("photoUrl").GetString()!;
        var photo = await anna.GetAsync(photoUrl, Cancel);
        Assert.Equal(HttpStatusCode.OK, photo.StatusCode);
        Assert.Equal("image/jpeg", photo.Content.Headers.ContentType?.MediaType);
        var bytes = await photo.Content.ReadAsByteArrayAsync(Cancel);
        using (var decoded = TestImages.Decode(bytes))
        {
            Assert.Equal(2400, Math.Max(decoded.Width, decoded.Height));
        }

        Assert.False(TestImages.ContainsSequence(bytes, "Exif\0\0"u8));
        Assert.False(TestImages.ContainsSequence(bytes, "SecretCam"u8));

        // Local storage (the default) keeps only the copy, under its own prefix.
        if (Environment.GetEnvironmentVariable(ExternalTestServices.S3UrlVariable) is null or "")
        {
            var folder = Path.Combine(host.DataDirectory, "receipts", "impressions", created.GetProperty("id").GetGuid().ToString("N"));
            Assert.Equal(["photo.jpg"], Directory.GetFiles(folder).Select(Path.GetFileName));
        }

        // A withdrawn impression no longer serves its photo.
        Assert.Equal(HttpStatusCode.NoContent, (await anna.DeleteAsync($"/api/impressions/{created.GetProperty("id").GetGuid()}", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anna.GetAsync(photoUrl, Cancel)).StatusCode);
    }

    [Fact]
    public async Task InvalidRequestsAreRejectedWithoutSavingAnything()
    {
        await using var host = new AuthTestHost();
        var purchase = await SaveAsync(
            host.Services,
            "Lidl",
            DateTimeOffset.UtcNow,
            ("SER", 7.99m),
            ("RABAT SER", -1.00m));
        var itemId = purchase.Items.First(item => item.Kind == Domain.Receipts.ReceiptLineKind.Product).Id;
        var discountId = purchase.Items.First(item => item.Kind == Domain.Receipts.ReceiptLineKind.Discount).Id;
        using var anna = host.CreateClientFor(Anna);

        await AssertProblemAsync(await CreateAsync(anna, itemId, "great"), HttpStatusCode.BadRequest, "rating");
        await AssertProblemAsync(await CreateAsync(anna, itemId, ""), HttpStatusCode.BadRequest, "rating");
        await AssertProblemAsync(await CreateAsync(anna, Guid.Empty, "like"), HttpStatusCode.BadRequest, "purchaseItemId");
        await AssertProblemAsync(await CreateAsync(anna, itemId, "like", new string('x', 2001)), HttpStatusCode.BadRequest, "2000");
        await AssertProblemAsync(await CreateAsync(anna, discountId, "like"), HttpStatusCode.BadRequest, "discount");
        await AssertProblemAsync(await CreateAsync(anna, Guid.NewGuid(), "like"), HttpStatusCode.NotFound, "was not found");
        await AssertProblemAsync(
            await CreateAsync(anna, itemId, "like", photo: ("notes.txt", "text/plain", "not an image"u8.ToArray())),
            HttpStatusCode.BadRequest,
            "JPEG, PNG and WebP");
        await AssertProblemAsync(
            await CreateAsync(anna, itemId, "like", photo: ("empty.jpg", "image/jpeg", [])),
            HttpStatusCode.BadRequest,
            "empty");
        await AssertProblemAsync(
            await anna.PostAsJsonAsync("/api/impressions", new { purchaseItemId = itemId, rating = "like" }, Cancel),
            HttpStatusCode.BadRequest,
            "multipart");
        await AssertProblemAsync(
            await anna.GetAsync("/api/impressions/unrated?days=0", Cancel),
            HttpStatusCode.BadRequest,
            "days");

        Assert.Empty((await anna.GetFromJsonAsync<JsonElement>($"/api/purchases/{purchase.PurchaseId}/impressions", Cancel)).EnumerateArray());
        if (Environment.GetEnvironmentVariable(ExternalTestServices.S3UrlVariable) is null or "")
        {
            Assert.False(Directory.Exists(Path.Combine(host.DataDirectory, "receipts", "impressions")));
        }
    }

    [Fact]
    public async Task RecentUnratedLinesArePersonalAndDisappearOnceRated()
    {
        await using var host = new AuthTestHost();
        var old = await SaveAsync(host.Services, "Lidl", DateTimeOffset.UtcNow.AddDays(-60), ("KAWA", 19.99m));
        var recent = await SaveAsync(host.Services, "Lidl", DateTimeOffset.UtcNow.AddDays(-1), ("CHLEB", 4.00m), ("MASLO", 7.49m), ("RABAT", -1.00m));
        using var anna = host.CreateClientFor(Anna);
        using var boris = host.CreateClientFor(Boris);

        var before = await anna.GetFromJsonAsync<JsonElement>("/api/impressions/unrated", Cancel);
        Assert.Equal(["CHLEB", "MASLO"], before.EnumerateArray().Select(item => item.GetProperty("itemName").GetString()));
        Assert.Equal("Lidl", before[0].GetProperty("merchantName").GetString());

        var chleb = recent.Items.Single(item => item.Name == "CHLEB").Id;
        Assert.Equal(HttpStatusCode.Created, (await CreateAsync(anna, chleb, "neutral")).StatusCode);

        var afterAnna = await anna.GetFromJsonAsync<JsonElement>("/api/impressions/unrated", Cancel);
        Assert.Equal(["MASLO"], afterAnna.EnumerateArray().Select(item => item.GetProperty("itemName").GetString()));
        // Boris has rated nothing, so his list is unchanged.
        Assert.Equal(2, (await boris.GetFromJsonAsync<JsonElement>("/api/impressions/unrated", Cancel)).GetArrayLength());
        // A longer window reaches the older purchase.
        var wide = await anna.GetFromJsonAsync<JsonElement>("/api/impressions/unrated?days=90&limit=10", Cancel);
        Assert.Contains(wide.EnumerateArray(), item => item.GetProperty("purchaseItemId").GetGuid() == old.Items.Single().Id);
    }

    [Fact]
    public async Task ImpressionsFollowTheirLineThroughAPurchaseCorrection()
    {
        await using var host = new AuthTestHost();
        var purchase = await SaveAsync(host.Services, "Lidl", DateTimeOffset.UtcNow, ("MLEKO", 3.00m), ("SER", 6.00m), ("JAJA", 9.00m));
        var productIds = purchase.Items.ToDictionary(item => item.Name, item => item.ProductId!.Value);
        using var anna = host.CreateClientFor(Anna);
        Assert.Equal(HttpStatusCode.Created, (await CreateAsync(anna, purchase.Items.Single(item => item.Name == "SER").Id, "like")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await CreateAsync(anna, purchase.Items.Single(item => item.Name == "JAJA").Id, "dislike")).StatusCode);

        // Remove MLEKO (shifting positions), re-price SER, drop JAJA entirely.
        var detail = await anna.GetFromJsonAsync<JsonElement>($"/api/purchases/{purchase.PurchaseId}", Cancel);
        var response = await anna.PutAsJsonAsync($"/api/purchases/{purchase.PurchaseId}", new
        {
            expectedUpdatedAt = detail.GetProperty("updatedAt").GetString(),
            merchantName = "Lidl",
            purchasedAt = detail.GetProperty("purchasedAt").GetString(),
            currency = "PLN",
            receiptTotal = 6.50m,
            items = new object[]
            {
                new { name = "SER", quantity = 1, unit = "szt", unitPrice = 6.50m, lineTotal = 6.50m, productLink = "existing", productId = productIds["SER"] }
            }
        }, Cancel);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        var newSerId = updated.GetProperty("items")[0].GetProperty("id").GetGuid();
        Assert.NotEqual(purchase.Items.Single(item => item.Name == "SER").Id, newSerId);

        var impressions = (await anna.GetFromJsonAsync<JsonElement>($"/api/purchases/{purchase.PurchaseId}/impressions", Cancel))
            .EnumerateArray().ToList();
        Assert.Equal(2, impressions.Count);
        var ser = impressions.Single(entry => entry.GetProperty("rating").GetString() == "like");
        Assert.Equal(newSerId, ser.GetProperty("purchaseItemId").GetGuid());
        Assert.Equal(6.50m, ser.GetProperty("unitPrice").GetDecimal());

        // The removed line's impression is kept, attached to the purchase and its product.
        var jaja = impressions.Single(entry => entry.GetProperty("rating").GetString() == "dislike");
        Assert.Equal(JsonValueKind.Null, jaja.GetProperty("purchaseItemId").ValueKind);
        Assert.Equal("JAJA", jaja.GetProperty("itemName").GetString());
        Assert.Equal(JsonValueKind.Null, jaja.GetProperty("unitPrice").ValueKind);
        var forProduct = await anna.GetFromJsonAsync<JsonElement>($"/api/products/{productIds["JAJA"]}/impressions", Cancel);
        Assert.Single(forProduct.EnumerateArray());
        Assert.Single((await anna.GetFromJsonAsync<JsonElement>($"/api/products/{productIds["SER"]}/impressions", Cancel)).EnumerateArray());
    }

    [Fact]
    public void RelinkingPrefersTheSameTextAtTheNearestPosition()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var newA = Guid.NewGuid();
        var newB1 = Guid.NewGuid();
        var newB2 = Guid.NewGuid();

        var moves = ImpressionRelinking.Match(
            [new(a, 0, "MLEKO", false), new(b, 2, "Ser ", false), new(c, 3, "RABAT", true)],
            [new(newB1, 0, "SER", false), new(newA, 1, "mleko", false), new(newB2, 2, "SER", false), new(Guid.NewGuid(), 3, "RABAT", true)]);

        Assert.Equal(newA, moves[a]);
        Assert.Equal(newB2, moves[b]);
        Assert.False(moves.ContainsKey(c));
    }

    [Fact]
    public void ALineTheEditorContinuesWinsOverTextAndIsNotTakenByIt()
    {
        var renamed = Guid.NewGuid();
        var deleted = Guid.NewGuid();
        var unrated = Guid.NewGuid();
        var toDiscount = Guid.NewGuid();
        var newRenamed = Guid.NewGuid();
        var newUnrated = Guid.NewGuid();
        var newDiscount = Guid.NewGuid();

        var moves = ImpressionRelinking.Match(
            [new(renamed, 0, "SER", false), new(deleted, 1, "MLEKO", false), new(unrated, 2, "JAJA", false), new(toDiscount, 3, "KAWA", false)],
            [new(newRenamed, 0, "SER GOUDA", false), new(newUnrated, 1, "MLEKO", false), new(newDiscount, 2, "KAWA", true)],
            new Dictionary<Guid, Guid> { [renamed] = newRenamed, [unrated] = newUnrated, [toDiscount] = newDiscount });

        // Renamed, yet the same line.
        Assert.Equal(newRenamed, moves[renamed]);
        // The deleted MLEKO does not take the row that continues JAJA, even with the same text.
        Assert.False(moves.ContainsKey(deleted));
        Assert.Equal(newUnrated, moves[unrated]);
        // A line turned into a discount loses its line link and does not fall back to text.
        Assert.False(moves.ContainsKey(toDiscount));
    }

    private static Task<HttpResponseMessage> CreateAsync(
        HttpClient client,
        Guid purchaseItemId,
        string rating,
        string? note = null,
        (string FileName, string ContentType, byte[] Bytes)? photo = null)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(purchaseItemId.ToString()), "purchaseItemId" },
            { new StringContent(rating), "rating" }
        };
        if (note is not null)
        {
            form.Add(new StringContent(note), "note");
        }

        if (photo is { } file)
        {
            var content = new ByteArrayContent(file.Bytes);
            content.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);
            form.Add(content, "photo", file.FileName);
        }

        return client.PostAsync("/api/impressions", form, Cancel);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string detailPart)
    {
        Assert.Equal(status, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonObject>(Cancel);
        Assert.Contains(detailPart, problem!["detail"]!.GetValue<string>(), StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<SavedPurchase> SaveAsync(
        IServiceProvider services,
        string merchant,
        DateTimeOffset purchasedAt,
        params (string Name, decimal Price)[] lines)
    {
        await using var scope = services.CreateAsyncScope();
        var view = await scope.ServiceProvider.GetRequiredService<IPurchaseDraftService>().CreateAsync(
            new CreatePurchaseDraftCommand(
                merchant,
                purchasedAt,
                "PLN",
                lines.Sum(line => line.Price),
                lines.Select(line => line.Price < 0
                    ? new PurchaseItemCommand(line.Name, null, null, null, line.Price, Domain.Receipts.ReceiptLineKind.Discount)
                    : new PurchaseItemCommand(line.Name, 1, "szt", line.Price, line.Price)).ToList(),
                AllowPossibleDuplicate: true),
            Cancel);

        var dbContext = scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>();
        var items = await dbContext.Set<Domain.Purchases.PurchaseItem>()
            .AsNoTracking()
            .Where(item => EF.Property<Guid>(item, "PurchaseDraftId") == view.PurchaseId)
            .OrderBy(item => item.Position)
            .ToListAsync(Cancel);
        return new SavedPurchase(view.PurchaseId, items);
    }

    private sealed record SavedPurchase(Guid PurchaseId, IReadOnlyList<Domain.Purchases.PurchaseItem> Items);
}
