using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PurchaseMemory.Application.Products;
using PurchaseMemory.Application.Purchases;
using PurchaseMemory.Domain.Products;
using PurchaseMemory.Domain.Purchases;
using PurchaseMemory.Infrastructure.Persistence;

namespace PurchaseMemory.IntegrationTests;

public sealed class ProductMatchingTests
{
    [Fact]
    public async Task AnEmptyCatalogBootstrapsItselfInsteadOfQueueingEveryLine()
    {
        await using var application = await TestApplication.CreateAsync();

        await SavePurchaseAsync(application, "Biedronka", "Ser Gouda plastry 150g", "Chleb żytni razowy");

        await using var scope = application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>();

        Assert.Equal(2, await dbContext.Products.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, await dbContext.ProductAliases.CountAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await dbContext.PendingProductMatches.ToListAsync(TestContext.Current.CancellationToken));
        Assert.All(
            await dbContext.Set<PurchaseItem>().ToListAsync(TestContext.Current.CancellationToken),
            item => Assert.NotNull(item.ProductId));
        Assert.All(
            await dbContext.ProductAliases.ToListAsync(TestContext.Current.CancellationToken),
            alias => Assert.Equal(ProductAliasSource.AutoCreated, alias.Source));
    }

    [Fact]
    public async Task TheSameWordingInTheSameStoreHitsTheAliasAndReusesTheProduct()
    {
        await using var application = await TestApplication.CreateAsync();

        await SavePurchaseAsync(application, "Biedronka", "Ser Gouda 250g");
        await SavePurchaseAsync(application, "BIEDRONKA", "SER GOUDA 250 G.");

        await using var scope = application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>();

        var products = await dbContext.Products.ToListAsync(TestContext.Current.CancellationToken);
        var items = await dbContext.Set<PurchaseItem>().ToListAsync(TestContext.Current.CancellationToken);

        Assert.Single(products);
        Assert.Single(await dbContext.ProductAliases.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, items.Count);
        Assert.All(items, item => Assert.Equal(products[0].Id, item.ProductId));
    }

    [Fact]
    public async Task AConfidentSimilarityMatchLinksToTheExistingProductAndRecordsAnAlias()
    {
        await using var application = await TestApplication.CreateAsync();

        await SavePurchaseAsync(application, "Biedronka", "Mleko Łaciate 3,2% 1L");

        // Polish receipts often append a VAT letter to the line; the wording is
        // otherwise identical, so this must link rather than fork the catalog.
        await SavePurchaseAsync(application, "Biedronka", "MLEKO LACIATE 3,2% 1L B");

        await using var scope = application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>();

        var products = await dbContext.Products.ToListAsync(TestContext.Current.CancellationToken);
        var aliases = await dbContext.ProductAliases
            .OrderBy(alias => alias.CreatedAt)
            .ToListAsync(TestContext.Current.CancellationToken);
        var items = await dbContext.Set<PurchaseItem>().ToListAsync(TestContext.Current.CancellationToken);

        Assert.Single(products);
        Assert.Equal(2, aliases.Count);
        Assert.Equal(ProductAliasSource.AutoCreated, aliases[0].Source);
        Assert.Equal(ProductAliasSource.AutoMatched, aliases[1].Source);
        Assert.All(items, item => Assert.Equal(products[0].Id, item.ProductId));
        Assert.Empty(await dbContext.PendingProductMatches.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AGreyZoneCandidateRaisesAPendingMatchInsteadOfGuessing()
    {
        await using var application = await TestApplication.CreateAsync();

        await SavePurchaseAsync(application, "Biedronka", "Ser Gouda plastry 150g");
        await SavePurchaseAsync(application, "Biedronka", "Ser Gouda plastry 250g");

        await using var scope = application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>();

        var pending = await dbContext.PendingProductMatches
            .Include(match => match.Candidates)
            .SingleAsync(TestContext.Current.CancellationToken);
        var unmatchedItem = await dbContext.Set<PurchaseItem>()
            .SingleAsync(item => item.Name == "Ser Gouda plastry 250g", TestContext.Current.CancellationToken);

        // A different package size is a different product until a person says otherwise.
        Assert.Single(await dbContext.Products.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Null(unmatchedItem.ProductId);
        Assert.Equal(unmatchedItem.Id, pending.PurchaseItemId);
        Assert.Equal("biedronka", pending.MerchantKey);
        Assert.Equal("ser gouda plastry 250 g", pending.NormalizedItemName);
        var candidate = Assert.Single(pending.Candidates);
        Assert.InRange(candidate.Score, ProductMatchingThresholds.PlausibleCandidate, 1.0);
    }

    [Fact]
    public async Task TwoEquallyPlausibleCandidatesAreLeftToAPerson()
    {
        await using var application = await TestApplication.CreateAsync();

        await SavePurchaseAsync(application, "Biedronka", "Woda Żywiec gazowana 1,5L");
        await SavePurchaseAsync(application, "Biedronka", "Woda Żywiec niegazowana 1,5L");

        await using var scope = application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>();

        var pending = await dbContext.PendingProductMatches
            .Include(match => match.Candidates)
            .SingleAsync(TestContext.Current.CancellationToken);

        Assert.Single(pending.Candidates);
        Assert.Single(await dbContext.Products.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UnrelatedNamesNeverShareAProductEvenWhenTheyStartWithTheSameWord()
    {
        await using var application = await TestApplication.CreateAsync();

        await SavePurchaseAsync(
            application,
            "Lidl",
            "Jogurt naturalny Zott",
            "Jogurt owocowy Zott");

        await using var scope = application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>();

        Assert.Equal(2, await dbContext.Products.CountAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await dbContext.PendingProductMatches.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TheSameProductInAnotherStoreGetsItsOwnAliasButNotItsOwnProduct()
    {
        await using var application = await TestApplication.CreateAsync();

        await SavePurchaseAsync(application, "Biedronka", "Ser Gouda 250g");
        await SavePurchaseAsync(application, "Lidl", "SER GOUDA 250 G");

        await using var scope = application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>();

        var aliases = await dbContext.ProductAliases.ToListAsync(TestContext.Current.CancellationToken);

        Assert.Single(await dbContext.Products.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, aliases.Count);
        Assert.Equal(["biedronka", "lidl"], aliases.Select(alias => alias.MerchantKey).Order());
    }

    [Fact]
    public async Task RepeatedMatchingOfTheSamePurchasesChangesNothing()
    {
        await using var application = await TestApplication.CreateAsync();

        await SavePurchaseAsync(application, "Biedronka", "Ser Gouda plastry 150g", "Chleb żytni razowy");
        await SavePurchaseAsync(application, "Biedronka", "Ser Gouda plastry 250g");

        var firstRun = await ReadCatalogStateAsync(application);
        await RunBackfillAsync(application);
        var secondRun = await ReadCatalogStateAsync(application);
        await RunBackfillAsync(application);
        var thirdRun = await ReadCatalogStateAsync(application);

        Assert.Equal(firstRun, secondRun);
        Assert.Equal(secondRun, thirdRun);
    }

    [Fact]
    public async Task TheCatalogReadPathReportsLinkedItemsAndThePendingQueueSize()
    {
        await using var application = await TestApplication.CreateAsync();

        await SavePurchaseAsync(application, "Biedronka", "Ser Gouda 250g", "Chleb żytni razowy");
        await SavePurchaseAsync(application, "Biedronka", "Ser Gouda 250 g");
        await SavePurchaseAsync(application, "Biedronka", "Ser Gouda 500g");

        await using var scope = application.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IProductCatalogRepository>();

        var page = await repository.ListProductsAsync(new ProductCatalogFilter(null), 1, 50, TestContext.Current.CancellationToken);
        var pendingCount = await repository.CountPendingMatchesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, page.TotalCount);
        Assert.False(page.HasMore);
        var gouda = page.Items.Single(entry => entry.NormalizedName == "ser gouda 250 g");
        Assert.Equal(2, gouda.LinkedItemCount);
        Assert.Equal(1, gouda.AliasCount);
        Assert.Equal(1, pendingCount);

        var filtered = await repository.ListProductsAsync(new ProductCatalogFilter("GOUDA"), 1, 50, TestContext.Current.CancellationToken);
        Assert.Equal(1, filtered.TotalCount);
    }

    [Fact]
    public async Task AMaximumLengthItemNameSavesEvenThoughNormalizationMakesItLonger()
    {
        await using var application = await TestApplication.CreateAsync();

        // A purchase item accepts 500 characters, and splitting digit runs from
        // letter runs nearly doubles that, so the catalog must truncate instead
        // of rejecting a purchase the user is allowed to save.
        var longName = string.Concat(Enumerable.Repeat("a1", 250));
        Assert.Equal(500, longName.Length);
        Assert.Equal(999, ProductNameNormalizer.Normalize(longName).Length);

        await SavePurchaseAsync(application, "Biedronka", longName);

        await using var scope = application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>();

        var product = await dbContext.Products.SingleAsync(TestContext.Current.CancellationToken);
        var item = await dbContext.Set<PurchaseItem>().SingleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(500, product.NormalizedName.Length);
        Assert.Equal(product.Id, item.ProductId);
        Assert.Equal(longName, item.Name);
    }

    [Fact]
    public async Task ADiscountLineNeverBecomesACatalogProduct()
    {
        await using var application = await TestApplication.CreateAsync();
        await using (var saveScope = application.Services.CreateAsyncScope())
        {
            await saveScope.ServiceProvider.GetRequiredService<IPurchaseDraftService>().CreateAsync(
                new CreatePurchaseDraftCommand(
                    "Lidl",
                    null,
                    "PLN",
                    69.99m,
                    [
                        new PurchaseItemCommand("Odkurzacz", 1, null, 99.99m, 99.99m),
                        new PurchaseItemCommand("OPUST", null, null, null, -30.00m)
                    ],
                    AllowPossibleDuplicate: true),
                TestContext.Current.CancellationToken);
        }

        await RunBackfillAsync(application);

        await using var scope = application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>();
        var product = await dbContext.Products.SingleAsync(TestContext.Current.CancellationToken);
        var alias = await dbContext.ProductAliases.SingleAsync(TestContext.Current.CancellationToken);
        var discount = await dbContext.Set<PurchaseItem>()
            .SingleAsync(item => item.Name == "OPUST", TestContext.Current.CancellationToken);

        Assert.Equal("Odkurzacz", product.CanonicalName);
        Assert.Equal(product.Id, alias.ProductId);
        Assert.Null(discount.ProductId);
        Assert.Equal(0, discount.DiscountedItemPosition);
        Assert.Empty(await dbContext.PendingProductMatches.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Throws<InvalidOperationException>(() => discount.LinkToProduct(product.Id));
    }

    private static async Task SavePurchaseAsync(
        TestApplication application,
        string merchantName,
        params string[] itemNames)
    {
        await using var scope = application.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IPurchaseDraftService>();

        await service.CreateAsync(
            new CreatePurchaseDraftCommand(
                merchantName,
                null,
                "PLN",
                null,
                itemNames
                    .Select(name => new PurchaseItemCommand(name, 1, "szt", 9.99m, 9.99m))
                    .ToList(),
                AllowPossibleDuplicate: true),
            TestContext.Current.CancellationToken);
    }

    /// <summary>Repeats what the backfill tool does, against the test database.</summary>
    private static async Task RunBackfillAsync(TestApplication application)
    {
        await using var scope = application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>();
        var matcher = scope.ServiceProvider.GetRequiredService<IProductMatcher>();

        var purchases = await dbContext.PurchaseDrafts
            .Include(purchase => purchase.Items)
            .OrderBy(purchase => purchase.CreatedAt)
            .ThenBy(purchase => purchase.Id)
            .ToListAsync(TestContext.Current.CancellationToken);

        foreach (var purchase in purchases)
        {
            await matcher.MatchAsync(purchase, TestContext.Current.CancellationToken);
        }
    }

    private static async Task<string> ReadCatalogStateAsync(TestApplication application)
    {
        await using var scope = application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>();

        var products = await dbContext.Products
            .OrderBy(product => product.NormalizedName)
            .Select(product => product.NormalizedName)
            .ToListAsync(TestContext.Current.CancellationToken);

        var aliases = await dbContext.ProductAliases
            .OrderBy(alias => alias.MerchantKey)
            .ThenBy(alias => alias.NormalizedText)
            .Select(alias => $"{alias.MerchantKey}|{alias.NormalizedText}|{alias.Source}")
            .ToListAsync(TestContext.Current.CancellationToken);

        var links = await dbContext.Set<PurchaseItem>()
            .OrderBy(item => item.Name)
            .Select(item => $"{item.Name}->{item.ProductId}")
            .ToListAsync(TestContext.Current.CancellationToken);

        var pending = await dbContext.PendingProductMatches
            .OrderBy(match => match.NormalizedItemName)
            .Select(match => match.NormalizedItemName)
            .ToListAsync(TestContext.Current.CancellationToken);

        return string.Join(
            "\n",
            [.. products, "--", .. aliases, "--", .. links, "--", .. pending]);
    }
}
