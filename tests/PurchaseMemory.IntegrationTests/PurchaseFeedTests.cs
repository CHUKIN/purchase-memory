using Microsoft.Extensions.DependencyInjection;
using PurchaseMemory.Application.Purchases;

namespace PurchaseMemory.IntegrationTests;

public sealed class PurchaseFeedTests
{
    private static readonly DateTimeOffset July1 = new(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset July5 = new(2026, 7, 5, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset July9 = new(2026, 7, 9, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task FeedOrdersByPurchasedAtDescendingAndPagesStably()
    {
        await using var application = await TestApplication.CreateAsync();
        await SeedAsync(
            application,
            Clean("Biedronka", July1),
            Clean("Lidl", July9),
            Clean("Żabka", July5));

        var firstPage = await GetFeedAsync(application, new PurchaseFeedQuery { PageSize = 2 });
        var secondPage = await GetFeedAsync(application, new PurchaseFeedQuery { PageSize = 2, Page = 2 });

        Assert.Equal(["Lidl", "Żabka"], firstPage.Items.Select(item => item.MerchantName));
        Assert.Equal(3, firstPage.TotalCount);
        Assert.True(firstPage.HasMore);
        Assert.False(firstPage.Truncated);

        Assert.Equal(["Biedronka"], secondPage.Items.Select(item => item.MerchantName));
        Assert.Equal(3, secondPage.TotalCount);
        Assert.False(secondPage.HasMore);
    }

    [Fact]
    public async Task FeedFallsBackToCreatedAtWhenPurchasedAtIsMissing()
    {
        await using var application = await TestApplication.CreateAsync();

        // The fixed test clock is 13 July 2026, so a purchase without a
        // recognized date sorts above every dated purchase in this fixture.
        await SeedAsync(
            application,
            Clean("Lidl", July9),
            Clean("Unknown date", purchasedAt: null));

        var feed = await GetFeedAsync(application, new PurchaseFeedQuery());

        Assert.Equal(["Unknown date", "Lidl"], feed.Items.Select(item => item.MerchantName));
    }

    [Fact]
    public async Task FeedFiltersByInclusiveDateRangeAndExcludesUndatedPurchases()
    {
        await using var application = await TestApplication.CreateAsync();
        await SeedAsync(
            application,
            Clean("Biedronka", July1),
            Clean("Żabka", July5),
            Clean("Lidl", July9),
            Clean("Unknown date", purchasedAt: null));

        var feed = await GetFeedAsync(
            application,
            new PurchaseFeedQuery { From = July1, To = July5 });

        Assert.Equal(["Żabka", "Biedronka"], feed.Items.Select(item => item.MerchantName));
        Assert.Equal(2, feed.TotalCount);
    }

    [Fact]
    public async Task FeedFiltersByReviewStatus()
    {
        await using var application = await TestApplication.CreateAsync();
        await SeedAsync(
            application,
            Clean("Lidl", July9),
            Mismatched("Biedronka", July5));

        var needsAttention = await GetFeedAsync(
            application,
            new PurchaseFeedQuery { Status = PurchaseReviewStatus.NeedsAttention });
        var reviewed = await GetFeedAsync(
            application,
            new PurchaseFeedQuery { Status = PurchaseReviewStatus.Reviewed });
        var all = await GetFeedAsync(application, new PurchaseFeedQuery());

        Assert.Equal("Biedronka", Assert.Single(needsAttention.Items).MerchantName);
        Assert.NotEmpty(Assert.Single(needsAttention.Items).Issues);
        Assert.Equal("Lidl", Assert.Single(reviewed.Items).MerchantName);
        Assert.Equal(2, all.TotalCount);
    }

    [Fact]
    public async Task FeedCombinesTextDateAndStatusFiltersWithAndSemantics()
    {
        await using var application = await TestApplication.CreateAsync();
        await SeedAsync(
            application,
            Mismatched("Biedronka Centrum", July5),
            Mismatched("Biedronka Centrum", July1),
            Mismatched("Lidl", July5),
            Clean("Biedronka Centrum", July5));

        var feed = await GetFeedAsync(
            application,
            new PurchaseFeedQuery
            {
                Query = "biedronka",
                From = July5,
                To = July9,
                Status = PurchaseReviewStatus.NeedsAttention
            });

        var match = Assert.Single(feed.Items);
        Assert.Equal("Biedronka Centrum", match.MerchantName);
        Assert.Equal(July5, match.PurchasedAt);
        Assert.NotEmpty(match.Issues);
    }

    [Fact]
    public async Task FeedFreeTextStillMatchesMerchantAndItemNamesIgnoringCase()
    {
        await using var application = await TestApplication.CreateAsync();
        await SeedAsync(
            application,
            new CreatePurchaseDraftCommand(
                "Русский магазин",
                July5,
                "PLN",
                5.25m,
                [new PurchaseItemCommand("Сыр сливочный", 1, "шт", 5.25m, 5.25m)]),
            Clean("Żabka", July9));

        var byItem = await GetFeedAsync(application, new PurchaseFeedQuery { Query = "СЫР" });
        var byMerchant = await GetFeedAsync(application, new PurchaseFeedQuery { Query = "żABKA" });
        var noMatch = await GetFeedAsync(application, new PurchaseFeedQuery { Query = "kawa" });

        Assert.Equal("Русский магазин", Assert.Single(byItem.Items).MerchantName);
        Assert.Equal("Żabka", Assert.Single(byMerchant.Items).MerchantName);
        Assert.Empty(noMatch.Items);
        Assert.Equal(0, noMatch.TotalCount);
    }

    [Fact]
    public async Task FeedRejectsInvalidPagingAndRanges()
    {
        await using var application = await TestApplication.CreateAsync();

        await Assert.ThrowsAsync<ArgumentException>(
            () => GetFeedAsync(application, new PurchaseFeedQuery { Page = 0 }));
        await Assert.ThrowsAsync<ArgumentException>(
            () => GetFeedAsync(application, new PurchaseFeedQuery { PageSize = 0 }));
        await Assert.ThrowsAsync<ArgumentException>(
            () => GetFeedAsync(application, new PurchaseFeedQuery { PageSize = 101 }));
        await Assert.ThrowsAsync<ArgumentException>(
            () => GetFeedAsync(application, new PurchaseFeedQuery { From = July9, To = July1 }));
        await Assert.ThrowsAsync<ArgumentException>(
            () => GetFeedAsync(application, new PurchaseFeedQuery { Query = new string('a', 201) }));
    }

    private static CreatePurchaseDraftCommand Clean(string merchant, DateTimeOffset? purchasedAt)
    {
        return new CreatePurchaseDraftCommand(
            merchant,
            purchasedAt,
            "PLN",
            10.00m,
            [new PurchaseItemCommand("MLEKO", 1, "pcs", 10.00m, 10.00m)]);
    }

    private static CreatePurchaseDraftCommand Mismatched(string merchant, DateTimeOffset? purchasedAt)
    {
        return new CreatePurchaseDraftCommand(
            merchant,
            purchasedAt,
            "PLN",
            25.00m,
            [new PurchaseItemCommand("MLEKO", 1, "pcs", 10.00m, 10.00m)]);
    }

    private static async Task SeedAsync(
        TestApplication application,
        params CreatePurchaseDraftCommand[] commands)
    {
        await using var scope = application.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IPurchaseDraftService>();

        foreach (var command in commands)
        {
            await service.CreateAsync(
                command with { AllowPossibleDuplicate = true },
                TestContext.Current.CancellationToken);
        }
    }

    private static async Task<PurchaseFeedPage> GetFeedAsync(
        TestApplication application,
        PurchaseFeedQuery query)
    {
        await using var scope = application.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IPurchaseDraftService>();
        return await service.GetFeedAsync(query, TestContext.Current.CancellationToken);
    }
}
