using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PurchaseMemory.Application.Products;
using PurchaseMemory.Application.Purchases;
using PurchaseMemory.Infrastructure.Persistence;

namespace PurchaseMemory.IntegrationTests;

/// <summary>
/// The read side behind <c>/products</c>: the searchable catalog and the single
/// product page, against a real SQLite database seeded through the normal save
/// path so the catalog links come from the deterministic matcher, not fixtures.
/// </summary>
public sealed class ProductCatalogQueryTests
{
    private static readonly DateTimeOffset July1 = new(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset July11 = new(2026, 7, 11, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset July21 = new(2026, 7, 21, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task TheCatalogIsFoundByTheWordingPrintedOnTheReceipt()
    {
        await using var application = await TestApplication.CreateAsync();
        await SaveAsync(application, "Biedronka", July1, Line("MLEKO LACIATE 3,2% 1L", 4.99m));
        await SaveAsync(application, "Lidl", July11, Line("Chleb żytni razowy", 6.50m));

        // The canonical name is whatever the first receipt printed, so searching
        // by the till wording has to work — that is how the owner remembers it.
        var byWording = await GetCatalogAsync(application, new ProductCatalogQuery { Query = "laciate" });
        var byDiacritics = await GetCatalogAsync(application, new ProductCatalogQuery { Query = "ŻYTNI" });
        var noMatch = await GetCatalogAsync(application, new ProductCatalogQuery { Query = "kawa" });

        Assert.Equal("MLEKO LACIATE 3,2% 1L", Assert.Single(byWording.Items).CanonicalName);
        Assert.Equal("Chleb żytni razowy", Assert.Single(byDiacritics.Items).CanonicalName);
        Assert.Empty(noMatch.Items);
    }

    [Fact]
    public async Task SearchAlsoMatchesAStoreSpecificAliasThatDiffersFromTheCanonicalName()
    {
        await using var application = await TestApplication.CreateAsync();
        await SaveAsync(application, "Biedronka", July1, Line("Mleko Łaciate 3,2% 1L", 4.99m));

        // A second store prints the same product differently; matching records it
        // as an alias, and searching by that wording must still find the product.
        await SaveAsync(application, "Lidl", July11, Line("MLEKO LACIATE 3,2% 1L B", 5.29m));

        var found = await GetCatalogAsync(application, new ProductCatalogQuery { Query = "laciate 3 2 1 l b" });

        var entry = Assert.Single(found.Items);
        Assert.Equal("Mleko Łaciate 3,2% 1L", entry.CanonicalName);
        Assert.Equal(2, entry.AliasCount);
        Assert.Equal(2, entry.LinkedItemCount);
    }

    [Fact]
    public async Task BothTheCatalogAndTheProductPageCountShoppingTripsNotReceiptLines()
    {
        await using var application = await TestApplication.CreateAsync();

        // Two jars of the same yoghurt on one receipt: two lines, one trip.
        await SaveAsync(
            application,
            "Biedronka",
            July1,
            new PurchaseItemCommand("Jogurt naturalny", 1, "szt", 3.49m, 3.49m),
            new PurchaseItemCommand("Jogurt naturalny", 1, "szt", 3.49m, 3.49m));

        var catalog = await GetCatalogAsync(application, new ProductCatalogQuery());
        var entry = Assert.Single(catalog.Items);
        Assert.Equal(1, entry.PurchaseCount);
        Assert.Equal(2, entry.LinkedItemCount);

        var detail = await GetProductAsync(application, entry.ProductId);
        Assert.NotNull(detail);
        Assert.Equal(1, detail.PurchaseCount);
        Assert.Equal(2, detail.LinkedItemCount);

        // The header count and the frequency block must never contradict.
        Assert.NotNull(detail.Frequency);
        Assert.Equal(detail.PurchaseCount, detail.Frequency.PurchaseCount);
    }

    [Fact]
    public async Task TheCatalogPagesStably()
    {
        await using var application = await TestApplication.CreateAsync();
        await SaveAsync(
            application,
            "Biedronka",
            July1,
            Line("Aronia suszona", 9.99m),
            Line("Bazylia świeża", 4.49m),
            Line("Cukier biały 1kg", 3.99m));

        var first = await GetCatalogAsync(application, new ProductCatalogQuery { PageSize = 2 });
        var second = await GetCatalogAsync(application, new ProductCatalogQuery { PageSize = 2, Page = 2 });

        Assert.Equal(["Aronia suszona", "Bazylia świeża"], first.Items.Select(item => item.CanonicalName));
        Assert.Equal(3, first.TotalCount);
        Assert.True(first.HasMore);
        Assert.Equal(["Cukier biały 1kg"], second.Items.Select(item => item.CanonicalName));
        Assert.False(second.HasMore);
    }

    [Fact]
    public async Task TheCatalogRejectsInvalidPagingAndOverlongSearches()
    {
        await using var application = await TestApplication.CreateAsync();

        await Assert.ThrowsAsync<ArgumentException>(
            () => GetCatalogAsync(application, new ProductCatalogQuery { Page = 0 }));
        await Assert.ThrowsAsync<ArgumentException>(
            () => GetCatalogAsync(application, new ProductCatalogQuery { PageSize = 0 }));
        await Assert.ThrowsAsync<ArgumentException>(
            () => GetCatalogAsync(application, new ProductCatalogQuery { PageSize = 201 }));
        await Assert.ThrowsAsync<ArgumentException>(
            () => GetCatalogAsync(application, new ProductCatalogQuery { Query = new string('a', 201) }));
    }

    [Fact]
    public async Task TheProductPageAnswersWhatItCostLastTimeAcrossStores()
    {
        await using var application = await TestApplication.CreateAsync();
        await SaveAsync(application, "Biedronka", July1, Line("Mleko Łaciate 3,2% 1L", 4.99m));
        await SaveAsync(application, "Lidl", July11, Line("MLEKO LACIATE 3,2% 1L B", 3.49m));
        await SaveAsync(application, "Żabka", July21, Line("Mleko Laciate 3,2% 1L", 6.30m));

        var detail = await GetProductAsync(application, await SingleProductIdAsync(application));

        Assert.NotNull(detail);
        Assert.Equal(3, detail.LinkedItemCount);
        Assert.Equal(0, detail.UnpricedItemCount);

        var series = Assert.Single(detail.PriceSeries);
        Assert.Equal("PLN", series.Currency);
        Assert.Equal(ProductUnit.Piece, series.Unit);
        Assert.Equal(6.30m, series.LatestPrice);
        Assert.Equal("Żabka", series.LatestPoint.MerchantName);
        Assert.Equal(3.49m, series.MinimumPrice);
        Assert.Equal(4.93m, series.AveragePrice);
        Assert.Equal([July1, July11, July21], series.Points.Select(point => point.PurchasedAt));

        Assert.NotNull(detail.Frequency);
        Assert.Equal(3, detail.Frequency.PurchaseCount);
        Assert.Equal(July1, detail.Frequency.FirstPurchasedAt);
        Assert.Equal(July21, detail.Frequency.LastPurchasedAt);

        Assert.Equal(3, detail.Aliases.Count);
        Assert.Contains(detail.Aliases, alias => alias.RawText == "MLEKO LACIATE 3,2% 1L B");
    }

    [Fact]
    public async Task AProductBoughtByWeightAndByPieceKeepsTheTwoPricesApart()
    {
        await using var application = await TestApplication.CreateAsync();
        await SaveAsync(
            application,
            "Biedronka",
            July1,
            new PurchaseItemCommand("Pomidory malinowe", 0.640m, "kg", 12.99m, 8.31m));
        await SaveAsync(
            application,
            "Biedronka",
            July11,
            new PurchaseItemCommand("Pomidory malinowe", 1m, "szt", 2.49m, 2.49m));

        var detail = await GetProductAsync(application, await SingleProductIdAsync(application));

        Assert.NotNull(detail);
        Assert.Equal(2, detail.PriceSeries.Count);
        Assert.Equal(
            [("kg", 12.99m), ("szt", 2.49m)],
            detail.PriceSeries
                .Select(series => (series.Unit, series.LatestPrice))
                .OrderBy(entry => entry.Unit, StringComparer.Ordinal));
    }

    [Fact]
    public async Task AProductBoughtOnceStillShowsItsOnlyPrice()
    {
        await using var application = await TestApplication.CreateAsync();
        await SaveAsync(application, "Biedronka", July1, Line("Kawa Jacobs 250g", 24.99m));

        var detail = await GetProductAsync(application, await SingleProductIdAsync(application));

        Assert.NotNull(detail);
        var series = Assert.Single(detail.PriceSeries);
        Assert.Equal(24.99m, series.LatestPrice);
        Assert.Equal(24.99m, series.MinimumPrice);
        Assert.Equal(24.99m, series.AveragePrice);
        Assert.NotNull(detail.Frequency);
        Assert.Equal(1, detail.Frequency.PurchaseCount);
        Assert.Null(detail.Frequency.AverageDaysBetweenPurchases);
    }

    [Fact]
    public async Task AnUnknownProductIsReportedAsMissingRatherThanEmpty()
    {
        await using var application = await TestApplication.CreateAsync();

        Assert.Null(await GetProductAsync(application, Guid.NewGuid()));
        await Assert.ThrowsAsync<ArgumentException>(() => GetProductAsync(application, Guid.Empty));
    }

    private static PurchaseItemCommand Line(string name, decimal price)
    {
        return new PurchaseItemCommand(name, 1, "szt", price, price);
    }

    private static async Task SaveAsync(
        TestApplication application,
        string merchant,
        DateTimeOffset purchasedAt,
        params PurchaseItemCommand[] items)
    {
        await using var scope = application.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IPurchaseDraftService>();
        await service.CreateAsync(
            new CreatePurchaseDraftCommand(
                merchant,
                purchasedAt,
                "PLN",
                items.Sum(item => item.LineTotal ?? 0m),
                items,
                AllowPossibleDuplicate: true),
            TestContext.Current.CancellationToken);
    }

    private static async Task<Guid> SingleProductIdAsync(TestApplication application)
    {
        await using var scope = application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>();
        var products = await dbContext.Products.ToListAsync(TestContext.Current.CancellationToken);
        return Assert.Single(products).Id;
    }

    private static async Task<ProductCatalogPage> GetCatalogAsync(
        TestApplication application,
        ProductCatalogQuery query)
    {
        await using var scope = application.Services.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<IProductCatalogService>();
        return await catalog.GetCatalogAsync(query, TestContext.Current.CancellationToken);
    }

    private static async Task<ProductDetail?> GetProductAsync(TestApplication application, Guid productId)
    {
        await using var scope = application.Services.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<IProductCatalogService>();
        return await catalog.GetProductAsync(productId, TestContext.Current.CancellationToken);
    }
}
