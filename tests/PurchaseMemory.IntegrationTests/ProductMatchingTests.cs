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
        Assert.False(await dbContext.Products.AnyAsync(product => product.NeedsReview, TestContext.Current.CancellationToken));
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
        Assert.False(products[0].NeedsReview);
        Assert.Empty(await dbContext.ProductReviewCandidates.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AGreyZoneCandidateGetsAFlaggedProductOfItsOwnInsteadOfAGuess()
    {
        await using var application = await TestApplication.CreateAsync();

        await SavePurchaseAsync(application, "Biedronka", "Ser Gouda plastry 150g");
        await SavePurchaseAsync(application, "Biedronka", "Ser Gouda plastry 250g");

        await using var scope = application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>();

        var existing = await dbContext.Products
            .SingleAsync(product => product.CanonicalName == "Ser Gouda plastry 150g", TestContext.Current.CancellationToken);
        var flagged = await dbContext.Products
            .SingleAsync(product => product.CanonicalName == "Ser Gouda plastry 250g", TestContext.Current.CancellationToken);
        var line = await dbContext.Set<PurchaseItem>()
            .SingleAsync(item => item.Name == "Ser Gouda plastry 250g", TestContext.Current.CancellationToken);

        // A different package size is a different product until a person says
        // otherwise — but the line is never left without a product.
        Assert.Equal(flagged.Id, line.ProductId);
        Assert.True(flagged.NeedsReview);
        Assert.Equal(ProductReviewReason.AmbiguousMatch, flagged.ReviewReason);
        Assert.Equal(ProductOrigin.Receipt, flagged.Origin);
        Assert.Equal("ser gouda plastry 250 g", flagged.NormalizedName);
        Assert.False(existing.NeedsReview);

        var alias = await dbContext.ProductAliases
            .SingleAsync(entry => entry.ProductId == flagged.Id, TestContext.Current.CancellationToken);
        Assert.Equal(("biedronka", "ser gouda plastry 250 g", ProductAliasSource.AutoCreated), (alias.MerchantKey, alias.NormalizedText, alias.Source));

        var candidate = await dbContext.ProductReviewCandidates.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal((flagged.Id, existing.Id, 0), (candidate.ProductId, candidate.CandidateProductId, candidate.Position));
        Assert.InRange(candidate.Score, ProductMatchingThresholds.PlausibleCandidate, ProductMatchingThresholds.ConfidentMatch);

        var audit = await dbContext.ProductAuditEntries
            .SingleAsync(entry => entry.ProductId == flagged.Id, TestContext.Current.CancellationToken);
        Assert.Equal((ProductAuditEntry.ReviewField, ProductAuditChangeSource.Matching, "system"), (audit.FieldName, audit.Source, audit.Actor));
    }

    [Fact]
    public async Task TheSameDoubtfulWordingLaterHitsTheFlaggedProductsAlias()
    {
        await using var application = await TestApplication.CreateAsync();

        await SavePurchaseAsync(application, "Biedronka", "ACTIV KEFI BOR 280");
        // Twice in one receipt and once more later: one flagged product, not three.
        await SavePurchaseAsync(application, "Biedronka", "ACTIV KEFI NAT 280", "ACTIV KEFI NAT 280");
        await SavePurchaseAsync(application, "Biedronka", "ACTIV KEFI NAT 280");

        await using var scope = application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>();

        var flagged = await dbContext.Products.SingleAsync(product => product.NeedsReview, TestContext.Current.CancellationToken);
        Assert.Equal("ACTIV KEFI NAT 280", flagged.CanonicalName);
        Assert.Equal(2, await dbContext.Products.CountAsync(TestContext.Current.CancellationToken));
        var lines = await dbContext.Set<PurchaseItem>()
            .Where(item => item.Name == "ACTIV KEFI NAT 280")
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(3, lines.Count);
        Assert.All(lines, item => Assert.Equal(flagged.Id, item.ProductId));
        Assert.Single(await dbContext.ProductReviewCandidates.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TwoEquallyPlausibleCandidatesAreLeftToAPerson()
    {
        await using var application = await TestApplication.CreateAsync();

        await SavePurchaseAsync(application, "Biedronka", "Woda Żywiec gazowana 1,5L");
        await SavePurchaseAsync(application, "Biedronka", "Woda Żywiec niegazowana 1,5L");

        await using var scope = application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>();

        var flagged = await dbContext.Products.SingleAsync(product => product.NeedsReview, TestContext.Current.CancellationToken);
        Assert.Equal("Woda Żywiec niegazowana 1,5L", flagged.CanonicalName);
        Assert.Equal(2, await dbContext.Products.CountAsync(TestContext.Current.CancellationToken));
        Assert.Single(await dbContext.ProductReviewCandidates.ToListAsync(TestContext.Current.CancellationToken));
        Assert.All(
            await dbContext.Set<PurchaseItem>().ToListAsync(TestContext.Current.CancellationToken),
            item => Assert.NotNull(item.ProductId));
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
        Assert.False(await dbContext.Products.AnyAsync(product => product.NeedsReview, TestContext.Current.CancellationToken));
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
    public async Task TheCatalogReadPathReportsLinkedItemsAndTheFlaggedProducts()
    {
        await using var application = await TestApplication.CreateAsync();

        await SavePurchaseAsync(application, "Biedronka", "Ser Gouda 250g", "Chleb żytni razowy");
        await SavePurchaseAsync(application, "Biedronka", "Ser Gouda 250 g");
        await SavePurchaseAsync(application, "Biedronka", "Ser Gouda 500g");

        await using var scope = application.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IProductCatalogRepository>();

        var page = await repository.ListProductsAsync(new ProductCatalogFilter(null), 1, 50, TestContext.Current.CancellationToken);
        var flaggedCount = await repository.CountProductsNeedingReviewAsync(TestContext.Current.CancellationToken);

        // The 500 g line is a product of its own now, carrying the flag.
        Assert.Equal(3, page.TotalCount);
        Assert.False(page.HasMore);
        var gouda = page.Items.Single(entry => entry.NormalizedName == "ser gouda 250 g");
        Assert.Equal(2, gouda.LinkedItemCount);
        Assert.Equal(1, gouda.AliasCount);
        Assert.False(gouda.NeedsReview);
        Assert.Equal(1, flaggedCount);
        Assert.True(page.Items.Single(entry => entry.NormalizedName == "ser gouda 500 g").NeedsReview);

        var filtered = await repository.ListProductsAsync(new ProductCatalogFilter("GOUDA"), 1, 50, TestContext.Current.CancellationToken);
        Assert.Equal(2, filtered.TotalCount);
        var onlyFlagged = await repository.ListProductsAsync(
            new ProductCatalogFilter(null, NeedsReview: true), 1, 50, TestContext.Current.CancellationToken);
        Assert.Equal("ser gouda 500 g", Assert.Single(onlyFlagged.Items).NormalizedName);
        var withoutGouda = await repository.ListProductsAsync(
            new ProductCatalogFilter("GOUDA", ExcludeProductId: gouda.ProductId), 1, 50, TestContext.Current.CancellationToken);
        Assert.DoesNotContain(withoutGouda.Items, entry => entry.ProductId == gouda.ProductId);
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
        Assert.False(await dbContext.Products.AnyAsync(entry => entry.NeedsReview, TestContext.Current.CancellationToken));
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

        var flagged = await dbContext.Products
            .Where(product => product.NeedsReview)
            .OrderBy(product => product.NormalizedName)
            .Select(product => product.NormalizedName)
            .ToListAsync(TestContext.Current.CancellationToken);
        var candidates = await dbContext.ProductReviewCandidates.CountAsync(TestContext.Current.CancellationToken);

        return string.Join(
            "\n",
            [.. products, "--", .. aliases, "--", .. links, "--", .. flagged, $"candidates={candidates}"]);
    }
}
