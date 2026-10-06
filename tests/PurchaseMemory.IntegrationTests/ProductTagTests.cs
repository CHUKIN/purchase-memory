using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PurchaseMemory.Application.Products;
using PurchaseMemory.Application.Products.Categories;
using PurchaseMemory.Application.Products.Tags;
using PurchaseMemory.Application.Purchases;
using PurchaseMemory.Application.Receipts;
using PurchaseMemory.Domain.Products;
using PurchaseMemory.Infrastructure.Persistence;

namespace PurchaseMemory.IntegrationTests;

/// <summary>
/// Household tags: CRUD, uniqueness, a product's tags, the audit trail and the
/// tag filter on the catalog and the purchase feed. Runs on SQLite and, opted
/// in, Postgres.
/// </summary>
public sealed class ProductTagTests
{
    private static readonly ReceiptActor Anna = new("user-anna", "anna@example.com");
    private static readonly Guid Dairy = ProductCategorySeed.IdFor(1);
    private static readonly Guid Milk = ProductCategorySeed.IdFor(2);
    private static readonly Guid Coffee = ProductCategorySeed.IdFor(34);

    private static readonly DateTimeOffset July1 = new(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset July5 = new(2026, 7, 5, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset July9 = new(2026, 7, 9, 10, 0, 0, TimeSpan.Zero);

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task NamesAreUniqueIgnoringCaseSpacesAndPolishLetters()
    {
        await using var application = await TestApplication.CreateAsync();
        await using var scope = application.Services.CreateAsyncScope();
        var tags = scope.ServiceProvider.GetRequiredService<IProductTagService>();

        var promo = await tags.CreateAsync("  Акция  ", Cancel);
        Assert.Equal("Акция", promo.Name);
        var city = await tags.CreateAsync("Łódź  trip", Cancel);
        Assert.Equal("Łódź trip", city.Name);

        await Assert.ThrowsAsync<ProductTagConflictException>(() => tags.CreateAsync("акция", Cancel));
        await Assert.ThrowsAsync<ProductTagConflictException>(() => tags.CreateAsync("LODZ TRIP", Cancel));
        await Assert.ThrowsAsync<ArgumentException>(() => tags.CreateAsync("   ", Cancel));
        await Assert.ThrowsAsync<ArgumentException>(() => tags.CreateAsync("!!!", Cancel));
        await Assert.ThrowsAsync<ArgumentException>(
            () => tags.CreateAsync(new string('a', ProductTag.MaximumNameLength + 1), Cancel));
        Assert.Equal(ProductTag.MaximumNameLength, (await tags.CreateAsync(new string('ß', ProductTag.MaximumNameLength), Cancel)).Name.Length);

        // A tag may change its own spelling, but not take another tag's name.
        var renamed = await tags.RenameAsync(promo.Id, "АКЦИЯ", Cancel);
        Assert.Equal("АКЦИЯ", renamed.Name);
        await Assert.ThrowsAsync<ProductTagConflictException>(() => tags.RenameAsync(promo.Id, "lodz trip", Cancel));
        await Assert.ThrowsAsync<ProductTagNotFoundException>(() => tags.RenameAsync(Guid.NewGuid(), "x", Cancel));
        await Assert.ThrowsAsync<ProductTagNotFoundException>(() => tags.DeleteAsync(Guid.NewGuid(), Anna, Cancel));

        var list = await tags.ListAsync(Cancel);
        Assert.Equal(["Łódź trip", "АКЦИЯ"], list.Where(tag => tag.Name.Length < 20).Select(tag => tag.Name));
        Assert.All(list, tag => Assert.Equal(0, tag.ProductCount));
    }

    [Fact]
    public async Task AProductsTagsAreReplacedAndTheChangeIsAudited()
    {
        await using var application = await TestApplication.CreateAsync();
        await SaveAsync(application, "Biedronka", July1, "Mleko Łaciate 3,2%");
        var milk = await ProductIdAsync(application, "Mleko Łaciate 3,2%");
        await using var scope = application.Services.CreateAsyncScope();
        var tags = scope.ServiceProvider.GetRequiredService<IProductTagService>();
        var kid = await tags.CreateAsync("для ребёнка", Cancel);
        var vegan = await tags.CreateAsync("веган", Cancel);
        var party = await tags.CreateAsync("к празднику", Cancel);

        var set = await tags.SetProductTagsAsync(milk, [party.Id, kid.Id, kid.Id], Anna, Cancel);
        Assert.Equal(["для ребёнка", "к празднику"], set.Select(tag => tag.Name));

        var counts = (await tags.ListAsync(Cancel)).ToDictionary(tag => tag.Name, tag => tag.ProductCount);
        Assert.Equal(1, counts["для ребёнка"]);
        Assert.Equal(0, counts["веган"]);

        var catalog = scope.ServiceProvider.GetRequiredService<IProductCatalogService>();
        var detail = await catalog.GetProductAsync(milk, Cancel);
        Assert.Equal(["для ребёнка", "к празднику"], detail!.Tags!.Select(tag => tag.Name));
        var page = await catalog.GetCatalogAsync(new ProductCatalogQuery(), Cancel);
        Assert.Equal(["для ребёнка", "к празднику"], Assert.Single(page.Items).Tags!.Select(tag => tag.Name));

        var replaced = await tags.SetProductTagsAsync(milk, [vegan.Id], Anna, Cancel);
        Assert.Equal(["веган"], replaced.Select(tag => tag.Name));
        // Setting the same tags again changes nothing and writes no audit entry.
        await tags.SetProductTagsAsync(milk, [vegan.Id], Anna, Cancel);
        var cleared = await tags.SetProductTagsAsync(milk, [], Anna, Cancel);
        Assert.Empty(cleared);

        var audit = (await scope.ServiceProvider.GetRequiredService<IProductCategoryService>()
            .GetAuditTrailAsync(milk, Cancel))
            .Where(entry => entry.FieldName == ProductAuditEntry.TagsField)
            .OrderBy(entry => entry.NewValue, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(3, audit.Count);
        Assert.All(audit, entry => Assert.Equal(ProductAuditChangeSource.Human, entry.Source));
        Assert.All(audit, entry => Assert.Equal("user-anna", entry.Actor));
        Assert.Contains(audit, entry => entry.OldValue == "—" && entry.NewValue == "для ребёнка, к празднику");
        Assert.Contains(audit, entry => entry.OldValue == "для ребёнка, к празднику" && entry.NewValue == "веган");
        Assert.Contains(audit, entry => entry.OldValue == "веган" && entry.NewValue == "—");
    }

    [Fact]
    public async Task SettingTagsValidatesTheRequest()
    {
        await using var application = await TestApplication.CreateAsync();
        await SaveAsync(application, "Biedronka", July1, "Mleko Łaciate 3,2%");
        var milk = await ProductIdAsync(application, "Mleko Łaciate 3,2%");
        await using var scope = application.Services.CreateAsyncScope();
        var tags = scope.ServiceProvider.GetRequiredService<IProductTagService>();
        var created = new List<Guid>();
        for (var index = 0; index <= ProductTagLink.MaximumTagsPerProduct; index++)
        {
            created.Add((await tags.CreateAsync($"тег {index}", Cancel)).Id);
        }

        await Assert.ThrowsAsync<ArgumentException>(() => tags.SetProductTagsAsync(milk, created, Anna, Cancel));
        Assert.Equal(ProductTagLink.MaximumTagsPerProduct,
            (await tags.SetProductTagsAsync(milk, created.Take(ProductTagLink.MaximumTagsPerProduct).ToList(), Anna, Cancel)).Count);
        await Assert.ThrowsAsync<ProductTagNotFoundException>(() => tags.SetProductTagsAsync(milk, [Guid.NewGuid()], Anna, Cancel));
        await Assert.ThrowsAsync<ArgumentException>(() => tags.SetProductTagsAsync(milk, [Guid.Empty], Anna, Cancel));
        await Assert.ThrowsAsync<ProductNotFoundException>(() => tags.SetProductTagsAsync(Guid.NewGuid(), [created[0]], Anna, Cancel));
    }

    [Fact]
    public async Task DeletingATagUnlinksItButKeepsTheProducts()
    {
        await using var application = await TestApplication.CreateAsync();
        await SaveAsync(application, "Biedronka", July1, "Mleko Łaciate 3,2%", "Kawa Lavazza 1kg");
        var milk = await ProductIdAsync(application, "Mleko Łaciate 3,2%");
        var coffee = await ProductIdAsync(application, "Kawa Lavazza 1kg");
        await using var scope = application.Services.CreateAsyncScope();
        var tags = scope.ServiceProvider.GetRequiredService<IProductTagService>();
        var promo = await tags.CreateAsync("акция", Cancel);
        var vegan = await tags.CreateAsync("веган", Cancel);
        await tags.SetProductTagsAsync(milk, [promo.Id, vegan.Id], Anna, Cancel);
        await tags.SetProductTagsAsync(coffee, [promo.Id], Anna, Cancel);

        var deletion = await tags.DeleteAsync(promo.Id, Anna, Cancel);
        Assert.Equal(2, deletion.UnlinkedProductCount);

        await using var read = application.Services.CreateAsyncScope();
        var catalog = read.ServiceProvider.GetRequiredService<IProductCatalogService>();
        Assert.Equal(["веган"], (await catalog.GetProductAsync(milk, Cancel))!.Tags!.Select(tag => tag.Name));
        Assert.Empty((await catalog.GetProductAsync(coffee, Cancel))!.Tags!);
        Assert.Equal(["веган"], (await read.ServiceProvider.GetRequiredService<IProductTagService>().ListAsync(Cancel)).Select(tag => tag.Name));
        var context = read.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>();
        Assert.Equal(1, await context.ProductTagLinks.CountAsync(Cancel));

        var audit = await read.ServiceProvider.GetRequiredService<IProductCategoryService>().GetAuditTrailAsync(milk, Cancel);
        Assert.Contains(audit, entry => entry.Source == ProductAuditChangeSource.TagDeleted
            && entry.OldValue == "акция, веган" && entry.NewValue == "веган");
        await Assert.ThrowsAsync<ProductTagNotFoundException>(
            () => GetFeedAsync(application, new PurchaseFeedQuery { TagIds = [promo.Id] }));
    }

    [Fact]
    public async Task TheTagFilterWorksAloneAndCombinedWithCategoryAndBrand()
    {
        await using var application = await TestApplication.CreateAsync();
        var milkTrip = await SaveAsync(application, "Biedronka", July1, "Mleko Łaciate 3,2%", "Chleb żytni");
        var cheeseTrip = await SaveAsync(application, "Lidl", July5, "Ser Gouda plastry");
        var coffeeTrip = await SaveAsync(application, "Żabka", July9, "Kawa Lavazza 1kg");
        await ClassifyAsync(application, "Mleko Łaciate 3,2%", Milk, "Łaciate");
        await ClassifyAsync(application, "Ser Gouda plastry", ProductCategorySeed.IdFor(3), "Hochland");
        await ClassifyAsync(application, "Kawa Lavazza 1kg", Coffee, "Lavazza");

        Guid kid, party;
        await using (var scope = application.Services.CreateAsyncScope())
        {
            var tags = scope.ServiceProvider.GetRequiredService<IProductTagService>();
            kid = (await tags.CreateAsync("для ребёнка", Cancel)).Id;
            party = (await tags.CreateAsync("к празднику", Cancel)).Id;
            await tags.SetProductTagsAsync(await ProductIdAsync(application, "Mleko Łaciate 3,2%"), [kid], Anna, Cancel);
            await tags.SetProductTagsAsync(await ProductIdAsync(application, "Ser Gouda plastry"), [kid, party], Anna, Cancel);
            await tags.SetProductTagsAsync(await ProductIdAsync(application, "Kawa Lavazza 1kg"), [party], Anna, Cancel);
        }

        var byKid = await GetFeedAsync(application, new PurchaseFeedQuery { TagIds = [kid] });
        Assert.Equal([cheeseTrip, milkTrip], byKid.Items.Select(item => item.PurchaseId));
        var milkPurchase = byKid.Items.Single(item => item.PurchaseId == milkTrip);
        Assert.Equal(
            [("Mleko Łaciate 3,2%", (bool?)true), ("Chleb żytni", false)],
            milkPurchase.Items.Select(item => (item.Name, item.MatchesFilter)));

        var both = await GetFeedAsync(application, new PurchaseFeedQuery { TagIds = [kid, party] });
        Assert.Equal([cheeseTrip], both.Items.Select(item => item.PurchaseId));

        var kidDairy = await GetFeedAsync(application, new PurchaseFeedQuery { TagIds = [kid], CategoryId = Milk });
        Assert.Equal([milkTrip], kidDairy.Items.Select(item => item.PurchaseId));
        var partyDairy = await GetFeedAsync(application, new PurchaseFeedQuery { TagIds = [party], CategoryId = Dairy });
        Assert.Equal([cheeseTrip], partyDairy.Items.Select(item => item.PurchaseId));
        var partyBrand = await GetFeedAsync(application, new PurchaseFeedQuery { TagIds = [party], Brand = "lavazza" });
        Assert.Equal([coffeeTrip], partyBrand.Items.Select(item => item.PurchaseId));
        Assert.Empty((await GetFeedAsync(application, new PurchaseFeedQuery { TagIds = [kid], Brand = "lavazza" })).Items);

        await using var read = application.Services.CreateAsyncScope();
        var catalog = read.ServiceProvider.GetRequiredService<IProductCatalogService>();
        var products = await catalog.GetCatalogAsync(new ProductCatalogQuery { TagIds = [kid] }, Cancel);
        Assert.Equal(2, products.TotalCount);
        var kidHochland = await catalog.GetCatalogAsync(new ProductCatalogQuery { TagIds = [kid], Brand = "HOCHLAND" }, Cancel);
        Assert.Equal(["Ser Gouda plastry"], kidHochland.Items.Select(item => item.CanonicalName));
        var partyCoffee = await catalog.GetCatalogAsync(new ProductCatalogQuery { TagIds = [party], CategoryId = Coffee }, Cancel);
        Assert.Equal(["Kawa Lavazza 1kg"], partyCoffee.Items.Select(item => item.CanonicalName));
        var kidUncategorized = await catalog.GetCatalogAsync(new ProductCatalogQuery { TagIds = [kid], Uncategorized = true }, Cancel);
        Assert.Empty(kidUncategorized.Items);

        await Assert.ThrowsAsync<ProductTagNotFoundException>(
            () => catalog.GetCatalogAsync(new ProductCatalogQuery { TagIds = [Guid.NewGuid()] }, Cancel));
        await Assert.ThrowsAsync<ProductTagNotFoundException>(
            () => GetFeedAsync(application, new PurchaseFeedQuery { TagIds = [Guid.NewGuid()] }));
    }

    [Fact]
    public async Task TheHttpTagEndpointsWorkEndToEnd()
    {
        await using var host = new AuthTestHost();
        using var client = host.CreateClientFor(AuthTestHost.Token());

        await using (var scope = host.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IPurchaseDraftService>().CreateAsync(
                new CreatePurchaseDraftCommand("Biedronka", July1, "PLN", null,
                    [new PurchaseItemCommand("Mleko 3,2% 1L", 1, "szt", 4.99m, 4.99m)], AllowPossibleDuplicate: true),
                Cancel);
        }

        var products = await client.GetFromJsonAsync<JsonElement>("/api/products", Cancel);
        var product = products.GetProperty("items")[0];
        var productId = product.GetProperty("id").GetString();
        Assert.Equal(0, product.GetProperty("tags").GetArrayLength());

        var created = await client.PostAsJsonAsync("/api/tags", new { name = "Для ребёнка" }, Cancel);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var tag = await created.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        var tagId = tag.GetProperty("id").GetString();
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PostAsJsonAsync("/api/tags", new { name = "для  РЕБЁНКА" }, Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/tags", new { name = " " }, Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/tags", new { name = new string('x', 41) }, Cancel)).StatusCode);

        var assigned = await client.PutAsJsonAsync($"/api/products/{productId}/tags", new { tagIds = new[] { tagId } }, Cancel);
        Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);
        var assignedBody = await assigned.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        Assert.Equal("Для ребёнка", assignedBody.GetProperty("tags")[0].GetProperty("name").GetString());
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync($"/api/products/{productId}/tags", new { tagIds = new[] { Guid.NewGuid() } }, Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PutAsJsonAsync($"/api/products/{Guid.NewGuid()}/tags", new { tagIds = new[] { tagId } }, Cancel)).StatusCode);

        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/products/{productId}", Cancel);
        Assert.Equal(tagId, detail.GetProperty("tags")[0].GetProperty("id").GetString());
        var list = await client.GetFromJsonAsync<JsonElement>("/api/tags", Cancel);
        Assert.Equal(1, list[0].GetProperty("productCount").GetInt32());

        var byTag = await client.GetFromJsonAsync<JsonElement>($"/api/products?tagId={tagId}", Cancel);
        Assert.Equal(1, byTag.GetProperty("totalCount").GetInt32());
        Assert.Equal("Для ребёнка", byTag.GetProperty("items")[0].GetProperty("tags")[0].GetProperty("name").GetString());
        var feed = await client.GetFromJsonAsync<JsonElement>($"/api/purchases?tagId={tagId}&categoryId={Dairy}", Cancel);
        Assert.Equal(0, feed.GetProperty("totalCount").GetInt32());
        feed = await client.GetFromJsonAsync<JsonElement>($"/api/purchases?tagId={tagId}", Cancel);
        Assert.Equal(1, feed.GetProperty("totalCount").GetInt32());
        Assert.True(feed.GetProperty("items")[0].GetProperty("items")[0].GetProperty("matchesFilter").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/products?tagId={Guid.NewGuid()}", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/purchases?tagId={Guid.NewGuid()}", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/purchases?tagId=not-a-guid", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/products?tagId=not-a-guid", Cancel)).StatusCode);
        // An empty value is «no tag filter», like an empty category.
        Assert.Equal(1, (await client.GetFromJsonAsync<JsonElement>("/api/purchases?tagId=", Cancel)).GetProperty("totalCount").GetInt32());
        Assert.Equal(1, (await client.GetFromJsonAsync<JsonElement>("/api/products?tagId=", Cancel)).GetProperty("totalCount").GetInt32());

        var renamed = await client.PutAsJsonAsync($"/api/tags/{tagId}", new { name = "Детям" }, Cancel);
        Assert.Equal("Детям", (await renamed.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("name").GetString());
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PutAsJsonAsync($"/api/tags/{Guid.NewGuid()}", new { name = "x" }, Cancel)).StatusCode);

        var deleted = await client.DeleteAsync($"/api/tags/{tagId}", Cancel);
        Assert.Equal(1, (await deleted.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("unlinkedProductCount").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/tags/{tagId}", Cancel)).StatusCode);
        var afterDelete = await client.GetFromJsonAsync<JsonElement>($"/api/products/{productId}", Cancel);
        Assert.Equal(0, afterDelete.GetProperty("tags").GetArrayLength());

        var audit = await client.GetFromJsonAsync<JsonElement>($"/api/products/{productId}/audit", Cancel);
        var sources = audit.EnumerateArray()
            .Where(entry => entry.GetProperty("fieldName").GetString() == "tags")
            .Select(entry => entry.GetProperty("source").GetString())
            .ToList();
        Assert.Equal(["tagDeleted", "human"], sources);
    }

    public static TheoryData<string, string> ProtectedCalls => new()
    {
        { "GET", "/api/tags" },
        { "POST", "/api/tags" },
        { "PUT", $"/api/tags/{Guid.NewGuid()}" },
        { "DELETE", $"/api/tags/{Guid.NewGuid()}" },
        { "PUT", $"/api/products/{Guid.NewGuid()}/tags" },
        { "GET", $"/api/products?tagId={Guid.NewGuid()}" },
        { "GET", $"/api/purchases?tagId={Guid.NewGuid()}" }
    };

    [Theory]
    [MemberData(nameof(ProtectedCalls))]
    public async Task EveryTagCallNeedsAnApprovedValidToken(string method, string path)
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
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(Request(method, path), Cancel)).StatusCode);
        }

        using var pending = host.CreateClientFor(AuthTestHost.Token(access: "pending"));
        Assert.Equal(HttpStatusCode.Forbidden, (await pending.SendAsync(Request(method, path), Cancel)).StatusCode);
    }

    private static HttpRequestMessage Request(string method, string path)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method is "POST" or "PUT")
        {
            request.Content = JsonContent.Create(new { name = "Проба", tagIds = Array.Empty<Guid>() });
        }

        return request;
    }

    private static async Task<Guid> SaveAsync(
        TestApplication application, string merchant, DateTimeOffset purchasedAt, params string[] itemNames)
    {
        await using var scope = application.Services.CreateAsyncScope();
        var view = await scope.ServiceProvider.GetRequiredService<IPurchaseDraftService>().CreateAsync(
            new CreatePurchaseDraftCommand(
                merchant, purchasedAt, "PLN", null,
                itemNames.Select(name => new PurchaseItemCommand(name, 1, "szt", 5m, 5m)).ToList(),
                AllowPossibleDuplicate: true),
            Cancel);
        return view.PurchaseId;
    }

    private static async Task<Guid> ProductIdAsync(TestApplication application, string itemName)
    {
        await using var scope = application.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>();
        var normalized = ProductNameNormalizer.Normalize(itemName);
        return (await context.Products.AsNoTracking().SingleAsync(product => product.NormalizedName == normalized, Cancel)).Id;
    }

    private static async Task ClassifyAsync(TestApplication application, string itemName, Guid categoryId, string brand)
    {
        var productId = await ProductIdAsync(application, itemName);
        await using var scope = application.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IProductCategoryService>().ClassifyProductAsync(
            new ClassifyProductCommand(productId, true, categoryId, true, brand), Anna, Cancel);
    }

    private static async Task<PurchaseFeedPage> GetFeedAsync(TestApplication application, PurchaseFeedQuery query)
    {
        await using var scope = application.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IPurchaseDraftService>().GetFeedAsync(query, Cancel);
    }
}
