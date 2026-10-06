using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PurchaseMemory.Application.Products;
using PurchaseMemory.Application.Products.Categories;
using PurchaseMemory.Application.Purchases;
using PurchaseMemory.Application.Receipts;
using PurchaseMemory.Domain.Products;
using PurchaseMemory.Infrastructure.Persistence;

namespace PurchaseMemory.IntegrationTests;

/// <summary>
/// Category and brand filters on the purchase feed and the brand list. Runs on
/// SQLite and, opted in, Postgres.
/// </summary>
public sealed class PurchaseFeedFilterTests
{
    private static readonly ReceiptActor Anna = new("user-anna", "anna@example.com");
    private static readonly Guid Dairy = ProductCategorySeed.IdFor(1);
    private static readonly Guid Milk = ProductCategorySeed.IdFor(2);
    private static readonly Guid Cheese = ProductCategorySeed.IdFor(3);
    private static readonly Guid Coffee = ProductCategorySeed.IdFor(34);

    private static readonly DateTimeOffset July1 = new(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset July5 = new(2026, 7, 5, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset July9 = new(2026, 7, 9, 10, 0, 0, TimeSpan.Zero);

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AGroupIncludesItsSubcategoriesAndASubcategoryStaysNarrow()
    {
        await using var application = await TestApplication.CreateAsync();
        var milkTrip = await SaveAsync(application, "Biedronka", July1, "Mleko Łaciate 3,2%", "Chleb żytni");
        var cheeseTrip = await SaveAsync(application, "Lidl", July5, "Ser Gouda plastry");
        var coffeeTrip = await SaveAsync(application, "Żabka", July9, "Kawa Lavazza 1kg");
        await ClassifyAsync(application, "Mleko Łaciate 3,2%", Milk, null);
        await ClassifyAsync(application, "Ser Gouda plastry", Cheese, null);
        await ClassifyAsync(application, "Kawa Lavazza 1kg", Coffee, null);

        var group = await GetFeedAsync(application, new PurchaseFeedQuery { CategoryId = Dairy });
        var milk = await GetFeedAsync(application, new PurchaseFeedQuery { CategoryId = Milk });

        Assert.Equal([cheeseTrip, milkTrip], group.Items.Select(item => item.PurchaseId));
        var onlyMilk = Assert.Single(milk.Items);
        Assert.Equal(milkTrip, onlyMilk.PurchaseId);

        // The whole receipt is shown, and the lines that matched are marked.
        Assert.Equal(
            [("Mleko Łaciate 3,2%", (bool?)true), ("Chleb żytni", false)],
            onlyMilk.Items.Select(item => (item.Name, item.MatchesFilter)));

        var unfiltered = await GetFeedAsync(application, new PurchaseFeedQuery());
        Assert.Equal(3, unfiltered.TotalCount);
        Assert.All(unfiltered.Items.SelectMany(item => item.Items), line => Assert.Null(line.MatchesFilter));
        Assert.DoesNotContain(coffeeTrip, group.Items.Select(item => item.PurchaseId));
    }

    [Fact]
    public async Task UncategorizedFindsPurchasesWithAnUnsortedProduct()
    {
        await using var application = await TestApplication.CreateAsync();
        var mixed = await SaveAsync(application, "Biedronka", July1, "Mleko Łaciate 3,2%", "Chleb żytni");
        await SaveAsync(application, "Lidl", July5, "Ser Gouda plastry");
        await ClassifyAsync(application, "Mleko Łaciate 3,2%", Milk, null);
        await ClassifyAsync(application, "Ser Gouda plastry", Cheese, null);

        var feed = await GetFeedAsync(application, new PurchaseFeedQuery { Uncategorized = true });

        var purchase = Assert.Single(feed.Items);
        Assert.Equal(mixed, purchase.PurchaseId);
        Assert.Equal([false, true], purchase.Items.Select(item => item.MatchesFilter ?? false));
    }

    [Fact]
    public async Task BrandIgnoresCaseAndPolishDiacriticsAndTheListGroupsSpellings()
    {
        await using var application = await TestApplication.CreateAsync();
        var first = await SaveAsync(application, "Biedronka", July1, "Mleko 3,2% 1L");
        var second = await SaveAsync(application, "Lidl", July5, "Masło extra 200g");
        await SaveAsync(application, "Żabka", July9, "Kawa ziarnista 1kg");
        await ClassifyAsync(application, "Mleko 3,2% 1L", null, "Łaciate");
        await ClassifyAsync(application, "Masło extra 200g", null, "ŁACIATE");
        await ClassifyAsync(application, "Kawa ziarnista 1kg", null, "Lavazza");

        foreach (var spelling in new[] { "łaciate", "LACIATE", "Łaciate", "  laciATE " })
        {
            var feed = await GetFeedAsync(application, new PurchaseFeedQuery { Brand = spelling });
            Assert.Equal([second, first], feed.Items.Select(item => item.PurchaseId));
        }

        await using var scope = application.Services.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<IProductCatalogService>();
        var byBrand = await catalog.GetCatalogAsync(new ProductCatalogQuery { Brand = "ŁaCiAtE" }, Cancel);
        Assert.Equal(2, byBrand.TotalCount);

        var brands = await catalog.ListBrandsAsync(Cancel);
        Assert.Equal(2, brands.Count);
        Assert.Equal(("laciate", 2), (brands[0].NormalizedBrand, brands[0].ProductCount));
        Assert.True(brands[0].Brand is "Łaciate" or "ŁACIATE", brands[0].Brand);
        Assert.Equal(("Lavazza", 1), (brands[1].Brand, brands[1].ProductCount));
    }

    [Fact]
    public async Task FiltersCombineWithTextDateAndEachOther()
    {
        await using var application = await TestApplication.CreateAsync();
        var early = await SaveAsync(application, "Biedronka", July1, "Mleko 3,2% 1L");
        var late = await SaveAsync(application, "Lidl", July9, "Mleko 3,2% 1L", "Ser Gouda plastry");
        await ClassifyAsync(application, "Mleko 3,2% 1L", Milk, "Łaciate");
        await ClassifyAsync(application, "Ser Gouda plastry", Cheese, "Hochland");

        var byDate = await GetFeedAsync(application, new PurchaseFeedQuery { CategoryId = Dairy, From = July5 });
        Assert.Equal([late], byDate.Items.Select(item => item.PurchaseId));

        var byText = await GetFeedAsync(application, new PurchaseFeedQuery { CategoryId = Milk, Query = "biedronka" });
        Assert.Equal([early], byText.Items.Select(item => item.PurchaseId));

        var categoryAndBrand = await GetFeedAsync(application, new PurchaseFeedQuery { CategoryId = Dairy, Brand = "hochland" });
        var match = Assert.Single(categoryAndBrand.Items);
        Assert.Equal(late, match.PurchaseId);
        Assert.Equal([false, true], match.Items.Select(item => item.MatchesFilter ?? false));

        var nothing = await GetFeedAsync(application, new PurchaseFeedQuery { CategoryId = Milk, Brand = "hochland" });
        Assert.Empty(nothing.Items);
        Assert.Equal(0, nothing.TotalCount);
    }

    [Fact]
    public async Task InvalidFiltersAreRejected()
    {
        await using var application = await TestApplication.CreateAsync();

        await Assert.ThrowsAsync<ProductCategoryNotFoundException>(
            () => GetFeedAsync(application, new PurchaseFeedQuery { CategoryId = Guid.NewGuid() }));
        await Assert.ThrowsAsync<ArgumentException>(
            () => GetFeedAsync(application, new PurchaseFeedQuery { CategoryId = Milk, Uncategorized = true }));
        await Assert.ThrowsAsync<ArgumentException>(
            () => GetFeedAsync(application, new PurchaseFeedQuery { Brand = new string('b', Product.MaximumBrandLength + 1) }));
    }

    [Fact]
    public async Task TheHttpFeedAndBrandListValidateAndFilter()
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
        var productId = products.GetProperty("items")[0].GetProperty("id").GetString();
        var classified = await client.PutAsJsonAsync(
            $"/api/products/{productId}/classification",
            new { setCategory = true, categoryId = Milk, setBrand = true, brand = "Łaciate" },
            Cancel);
        Assert.Equal(HttpStatusCode.OK, classified.StatusCode);

        var byGroup = await client.GetFromJsonAsync<JsonElement>($"/api/purchases?categoryId={Dairy}&brand=LACIATE", Cancel);
        Assert.Equal(1, byGroup.GetProperty("totalCount").GetInt32());
        Assert.True(byGroup.GetProperty("items")[0].GetProperty("items")[0].GetProperty("matchesFilter").GetBoolean());

        var other = await client.GetFromJsonAsync<JsonElement>($"/api/purchases?categoryId={Coffee}", Cancel);
        Assert.Equal(0, other.GetProperty("totalCount").GetInt32());

        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.GetAsync($"/api/purchases?categoryId={Guid.NewGuid()}", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.GetAsync($"/api/purchases?categoryId={Milk}&uncategorized=true", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.GetAsync("/api/purchases?categoryId=not-a-guid", Cancel)).StatusCode);

        var brands = await client.GetFromJsonAsync<JsonElement>("/api/brands", Cancel);
        Assert.Equal("Łaciate", brands[0].GetProperty("brand").GetString());
        Assert.Equal(1, brands[0].GetProperty("productCount").GetInt32());
    }

    [Fact]
    public async Task TheBrandListNeedsAnApprovedUser()
    {
        await using var host = new AuthTestHost();
        using var anonymous = host.CreateClientFor(null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/brands", Cancel)).StatusCode);
        using var forged = host.CreateClientFor(AuthTestHost.Token(key: AuthTestHost.ForeignKey));
        Assert.Equal(HttpStatusCode.Unauthorized, (await forged.GetAsync("/api/brands", Cancel)).StatusCode);
        using var pending = host.CreateClientFor(AuthTestHost.Token(access: "pending"));
        Assert.Equal(HttpStatusCode.Forbidden, (await pending.GetAsync("/api/brands", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await pending.GetAsync($"/api/purchases?categoryId={Milk}", Cancel)).StatusCode);
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

    private static async Task ClassifyAsync(TestApplication application, string itemName, Guid? categoryId, string? brand)
    {
        await using var scope = application.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>();
        var normalized = ProductNameNormalizer.Normalize(itemName);
        var productId = (await context.Products.AsNoTracking().SingleAsync(product => product.NormalizedName == normalized, Cancel)).Id;
        await scope.ServiceProvider.GetRequiredService<IProductCategoryService>().ClassifyProductAsync(
            new ClassifyProductCommand(productId, categoryId is not null, categoryId, brand is not null, brand), Anna, Cancel);
    }

    private static async Task<PurchaseFeedPage> GetFeedAsync(TestApplication application, PurchaseFeedQuery query)
    {
        await using var scope = application.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IPurchaseDraftService>().GetFeedAsync(query, Cancel);
    }
}
