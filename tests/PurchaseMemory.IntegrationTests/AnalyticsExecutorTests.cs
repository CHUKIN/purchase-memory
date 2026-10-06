using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PurchaseMemory.Application.Analytics;
using PurchaseMemory.Application.Products;
using PurchaseMemory.Application.Products.Categories;
using PurchaseMemory.Application.Products.Tags;
using PurchaseMemory.Application.Purchases;
using PurchaseMemory.Application.Receipts;
using PurchaseMemory.Domain.Products;
using PurchaseMemory.Domain.Receipts;
using PurchaseMemory.Infrastructure.Persistence;

namespace PurchaseMemory.IntegrationTests;

/// <summary>
/// The analytics executor on seeded purchases, through the real repository.
/// Runs on SQLite and, with PURCHASE_MEMORY_TEST_POSTGRES, on Postgres. «Today»
/// is 13 July 2026 (the test application's clock), Europe/Warsaw.
/// </summary>
public sealed class AnalyticsExecutorTests
{
    private static readonly ReceiptActor Anna = new("user-anna", "anna@example.com");
    private static readonly Guid Dairy = ProductCategorySeed.IdFor(1);
    private static readonly Guid Milk = ProductCategorySeed.IdFor(2);
    private static readonly Guid Cheese = ProductCategorySeed.IdFor(3);
    private static readonly Guid Coffee = ProductCategorySeed.IdFor(34);
    private static readonly DateOnly YearStart = new(2026, 1, 1);
    private static readonly DateOnly Today = new(2026, 7, 13);

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SpendCountsWhatWasPaidIncludingProductAndReceiptDiscounts()
    {
        await using var application = await SeedAsync();

        var all = await RunAsync(application, new AnalyticsPlan { From = YearStart, To = Today });
        // P1: 8 − 1 + 50, P2: 4.50 + 15, P3: 10 + 60 − 5 (receipt-level coupon).
        Assert.Equal(141.50m, Kpi(all, "spend"));
        Assert.Equal(-6.00m, Kpi(all, "discounts"));
        Assert.Equal(3m, Kpi(all, "purchaseCount"));
        Assert.Equal(6m, Kpi(all, "lineCount"));
        Assert.Equal("PLN", all.Result.Currency);
        Assert.Equal("Потрачено 141,50 zł: 6 позиций в 3 чеках. Скидки сэкономили 6,00 zł.", all.Conclusion);

        var biedronkaJuly = await RunAsync(application, new AnalyticsPlan
        {
            Merchants = ["BIEDRONKA"],
            From = new DateOnly(2026, 7, 1),
            To = new DateOnly(2026, 7, 31)
        });
        Assert.Equal(65.00m, Kpi(biedronkaJuly, "spend"));
        Assert.Equal(["Biedronka"], biedronkaJuly.Plan.Merchants);
        Assert.Equal(Today, biedronkaJuly.Plan.To);
        var evidence = Assert.Single(biedronkaJuly.Result.Evidence);
        Assert.Equal(65.00m, evidence.Amount);
        Assert.Equal(2, evidence.MatchedLineCount);
    }

    [Fact]
    public async Task ItemQuestionsKeepLinkedDiscountsButNotReceiptCoupons()
    {
        await using var application = await SeedAsync();

        var coffee = await RunAsync(application, new AnalyticsPlan { NameTerms = ["kawa"], From = new DateOnly(2026, 7, 1) });
        Assert.Equal(60.00m, Kpi(coffee, "spend"));
        Assert.Equal(1, coffee.Result.Coverage.UnlinkedDiscountsExcluded);

        var dairy = await RunAsync(application, new AnalyticsPlan { CategoryIds = [Dairy], From = YearStart });
        // The group includes Молоко and Сыр: 8 − 1, 4.50 and 15.
        Assert.Equal(26.50m, Kpi(dairy, "spend"));
        Assert.Equal(1, dairy.Result.Coverage.UncategorizedLinesInScope);
        Assert.Equal(3, dairy.Result.Coverage.PurchasesInScope);

        var discounted = await RunAsync(application, new AnalyticsPlan { CategoryIds = [Milk], Discount = AnalyticsDiscountFilter.DiscountedOnly });
        Assert.Equal(7.00m, Kpi(discounted, "spend"));
        var full = await RunAsync(application, new AnalyticsPlan
        {
            CategoryIds = [Milk], Discount = AnalyticsDiscountFilter.NotDiscounted, From = YearStart
        });
        Assert.Equal(4.50m, Kpi(full, "spend"));
    }

    [Fact]
    public async Task QuantityPriceCountAndExistence()
    {
        await using var application = await SeedAsync();

        var milk = await RunAsync(application, new AnalyticsPlan { Metric = AnalyticsMetric.Quantity, CategoryIds = [Milk] });
        Assert.Equal(4m, Kpi(milk, "quantity"));
        Assert.Equal("szt", milk.Result.Kpis.Single(kpi => kpi.Code == "quantity").Unit);
        Assert.Equal(3m, Kpi(milk, "lineCount"));

        var cheese = await RunAsync(application, new AnalyticsPlan { Metric = AnalyticsMetric.Quantity, NameTerms = ["gouda"] });
        Assert.Equal(0.5m, Kpi(cheese, "quantity"));
        Assert.Equal("Куплено 0,5 кг: 1 позиция в 1 чеке.", cheese.Conclusion);

        var coffeePrice = await RunAsync(application, new AnalyticsPlan { Metric = AnalyticsMetric.AverageUnitPrice, Brands = ["lavazza"] });
        Assert.Equal((55m, 50m, 60m), (Kpi(coffeePrice, "averagePrice"), Kpi(coffeePrice, "minimumPrice"), Kpi(coffeePrice, "maximumPrice")));
        Assert.Equal(["Lavazza"], coffeePrice.Plan.Brands);

        // Shelf price before discounts: 4.00, 4.50, 3.99.
        var milkPrice = await RunAsync(application, new AnalyticsPlan { Metric = AnalyticsMetric.AverageUnitPrice, CategoryIds = [Milk] });
        Assert.Equal(4.16m, Kpi(milkPrice, "averagePrice"));
        var cheapest = await RunAsync(application, new AnalyticsPlan { Metric = AnalyticsMetric.MinimumUnitPrice, CategoryIds = [Milk] });
        Assert.Equal(3.99m, Kpi(cheapest, "minimumPrice"));
        var dearest = await RunAsync(application, new AnalyticsPlan { Metric = AnalyticsMetric.MaximumUnitPrice, CategoryIds = [Milk] });
        Assert.Equal(4.50m, Kpi(dearest, "maximumPrice"));

        var count = await RunAsync(application, new AnalyticsPlan { Metric = AnalyticsMetric.PurchaseCount, NameTerms = ["mleko"] });
        Assert.Equal(3m, Kpi(count, "purchaseCount"));

        var yes = await RunAsync(application, new AnalyticsPlan { Metric = AnalyticsMetric.Exists, CategoryIds = [Milk], GroupBy = AnalyticsGroupBy.Month });
        Assert.Equal(1m, Kpi(yes, "exists"));
        Assert.Equal(new DateOnly(2026, 7, 2), yes.Result.Kpis.Single(kpi => kpi.Code == "lastPurchase").Date);
        Assert.Equal(AnalyticsGroupBy.None, yes.Plan.GroupBy);
        Assert.StartsWith("Да, покупали: 3 чека", yes.Conclusion, StringComparison.Ordinal);

        var no = await RunAsync(application, new AnalyticsPlan { Metric = AnalyticsMetric.Exists, NameTerms = ["makaron pelnoziarnisty"] });
        Assert.False(no.Result.HasData);
        Assert.Equal(0m, Kpi(no, "exists"));
        Assert.Equal("Нет, таких покупок не нашлось.", no.Conclusion);
    }

    [Fact]
    public async Task GroupsByEveryDimension()
    {
        await using var application = await SeedAsync();

        var months = await RunAsync(application, new AnalyticsPlan { From = YearStart, To = Today, GroupBy = AnalyticsGroupBy.Month });
        Assert.Equal(["2026-01", "2026-02", "2026-03", "2026-04", "2026-05", "2026-06", "2026-07"], months.Result.Groups.Select(group => group.Key));
        Assert.Equal(57.00m, months.Result.Groups[5].Value);
        Assert.Equal(84.50m, months.Result.Groups[6].Value);
        Assert.Equal(0m, months.Result.Groups[0].Value);

        var weeks = await RunAsync(application, new AnalyticsPlan
        {
            From = new DateOnly(2026, 6, 29), To = Today, GroupBy = AnalyticsGroupBy.Week
        });
        Assert.Equal(["2026-06-29", "2026-07-06", "2026-07-13"], weeks.Result.Groups.Select(group => group.Key));
        Assert.Equal([19.50m, 65.00m, 0m], weeks.Result.Groups.Select(group => group.Value));

        var merchants = await RunAsync(application, new AnalyticsPlan { From = YearStart, GroupBy = AnalyticsGroupBy.Merchant });
        Assert.Equal([("Biedronka", 122.00m), ("Lidl", 19.50m)], merchants.Result.Groups.Select(group => (group.Label, group.Value)));

        var categories = await RunAsync(application, new AnalyticsPlan { From = YearStart, GroupBy = AnalyticsGroupBy.Category });
        Assert.Equal(
            [("coffee", 110.00m), ("cheese", 15.00m), ("milk", 11.50m), (null, 10.00m), ("receiptDiscount", -5.00m)],
            categories.Result.Groups.Select(group => (group.CategorySystemKey, group.Value)));

        var brands = await RunAsync(application, new AnalyticsPlan { CategoryIds = [Milk, Coffee], GroupBy = AnalyticsGroupBy.Brand });
        Assert.Equal([("Lavazza", 110.00m), ("Łaciate", 15.49m)], brands.Result.Groups.Select(group => (group.Label, group.Value)));

        var top = await RunAsync(application, new AnalyticsPlan { Metric = AnalyticsMetric.Top, From = YearStart, Limit = 2 });
        Assert.Equal(AnalyticsGroupBy.Product, top.Plan.GroupBy);
        Assert.Equal([("KAWA LAVAZZA 1KG", 110.00m), ("SER GOUDA", 15.00m)], top.Result.Groups.Select(group => (group.Label, group.Value)));
        Assert.NotNull(top.Result.Groups[0].ProductId);
        Assert.Equal(3, top.Result.HiddenGroupCount);
        Assert.StartsWith("Больше всего потрачено на «KAWA LAVAZZA 1KG»: 110,00", top.Conclusion, StringComparison.Ordinal);

        var quantityByMonth = await RunAsync(application, new AnalyticsPlan
        {
            Metric = AnalyticsMetric.Quantity, CategoryIds = [Milk], From = YearStart, GroupBy = AnalyticsGroupBy.Month
        });
        Assert.Equal([2m, 1m], quantityByMonth.Result.Groups.Skip(5).Select(group => group.Value));
    }

    [Fact]
    public async Task NoDataIsSaidPlainlyAndUnknownFiltersAreDropped()
    {
        await using var application = await SeedAsync();

        var empty = await RunAsync(application, new AnalyticsPlan { From = new DateOnly(2024, 1, 1), To = new DateOnly(2024, 12, 31) });
        Assert.False(empty.Result.HasData);
        Assert.Equal(0, empty.Result.Coverage.PurchasesInScope);
        Assert.Empty(empty.Result.Evidence);
        Assert.Equal("По этим условиям покупок не нашлось.", empty.Conclusion);

        var english = await RunAsync(application, new AnalyticsPlan { From = new DateOnly(2024, 1, 1), To = new DateOnly(2024, 12, 31) }, "en");
        Assert.Equal("No purchases match these filters.", english.Conclusion);

        var unknownCategory = Guid.NewGuid();
        var cleaned = await RunAsync(application, new AnalyticsPlan
        {
            CategoryIds = [unknownCategory, Milk],
            ProductIds = [Guid.NewGuid()],
            Merchants = ["Carrefour"],
            From = Today,
            To = YearStart
        });
        Assert.Equal([Milk], cleaned.Plan.CategoryIds);
        Assert.Empty(cleaned.Plan.ProductIds);
        Assert.Empty(cleaned.Plan.Merchants);
        Assert.Equal((YearStart, Today), (cleaned.Plan.From!.Value, cleaned.Plan.To!.Value));
        Assert.Equal(
            [AnalyticsNote.UnknownCategory, AnalyticsNote.UnknownProduct, AnalyticsNote.UnknownMerchant, AnalyticsNote.PeriodSwapped],
            cleaned.Notes.Select(note => note.Code));
        Assert.Equal(11.50m, Kpi(cleaned, "spend"));
    }

    [Fact]
    public async Task ProductIdsSelectExactProducts()
    {
        await using var application = await SeedAsync();
        Guid coffeeId;
        await using (var scope = application.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>();
            coffeeId = (await context.Products.AsNoTracking().SingleAsync(product => product.NormalizedName.StartsWith("kawa"), Cancel)).Id;
        }

        var answer = await RunAsync(application, new AnalyticsPlan { ProductIds = [coffeeId] });
        Assert.Equal(110.00m, Kpi(answer, "spend"));
        Assert.All(answer.Result.Evidence, purchase => Assert.All(purchase.Items, item => Assert.Equal(coffeeId, item.ProductId)));
    }

    [Fact]
    public void DiscountPointingAtAMissingLineIsAWholeReceiptDiscount()
    {
        var purchase = new AnalyticsPurchaseFact(Guid.NewGuid(), null, new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero), true, "Lidl", "PLN",
        [
            new AnalyticsLineFact(0, "CHLEB", 1, null, 10m, 10m, false, null, null, null, null, null, null),
            new AnalyticsLineFact(1, "RABAT", null, null, null, -2m, true, 7, null, null, null, null, null)
        ]);
        var vocabulary = new AnalyticsVocabulary([], ["Lidl"], []);
        var warsaw = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");

        var whole = AnalyticsCalculator.Calculate(new AnalyticsPlan(), [purchase], vocabulary, warsaw, false);
        Assert.Equal(8m, whole.Kpis.Single(kpi => kpi.Code == "spend").Value);

        var bread = AnalyticsCalculator.Calculate(new AnalyticsPlan { NameTerms = ["chleb"] }, [purchase], vocabulary, warsaw, false);
        Assert.Equal(10m, bread.Kpis.Single(kpi => kpi.Code == "spend").Value);
        Assert.Equal(1, bread.Coverage.UnlinkedDiscountsExcluded);
    }

    [Fact]
    public void LinesWithoutAQuantityAreCountedAsOnePieceAndReported()
    {
        var purchase = new AnalyticsPurchaseFact(Guid.NewGuid(), null, new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero), true, "Lidl", "PLN",
        [
            new AnalyticsLineFact(0, "JOGURT", 3, "szt", 2m, 6m, false, null, null, null, null, null, null),
            new AnalyticsLineFact(1, "JOGURT", null, null, null, 2m, false, null, null, null, null, null, null)
        ]);
        var result = AnalyticsCalculator.Calculate(
            new AnalyticsPlan { Metric = AnalyticsMetric.Quantity, NameTerms = ["jogurt"] }, [purchase],
            new AnalyticsVocabulary([], [], []), TimeZoneInfo.Utc, false);
        Assert.Equal(4m, result.Kpis.Single(kpi => kpi.Code == "quantity").Value);
        Assert.Equal(1, result.Coverage.AssumedQuantityLines);
    }

    [Fact]
    public async Task DaysAreCountedInTheHouseholdsTimeZone()
    {
        await using var application = await TestApplication.CreateAsync();
        // 23:30 UTC on 31 December is already New Year's Day in Warsaw.
        await SaveAsync(application, "Żabka", new DateTimeOffset(2025, 12, 31, 23, 30, 0, TimeSpan.Zero),
            new PurchaseItemCommand("WODA", 1, null, 2.00m, 2.00m));

        var lastYear = await RunAsync(application, new AnalyticsPlan { From = new DateOnly(2025, 1, 1), To = new DateOnly(2025, 12, 31) });
        var thisYear = await RunAsync(application, new AnalyticsPlan { From = YearStart, To = Today, GroupBy = AnalyticsGroupBy.Month });
        Assert.False(lastYear.Result.HasData);
        Assert.Equal(2.00m, Kpi(thisYear, "spend"));
        Assert.Equal(2.00m, thisYear.Result.Groups[0].Value);
    }

    [Fact]
    public async Task TagFilterNeedsEveryTagAndNarrowsCategoriesPeriodsAndStores()
    {
        await using var application = await SeedAsync();
        var (kid, promo, vegan) = await TagAsync(application);

        // Milk carries «Для ребёнка»: 8 − 1 and 4.50 this year, 3.99 last December.
        var kidThisYear = await RunAsync(application, new AnalyticsPlan { TagIds = [kid], From = YearStart, To = Today });
        Assert.Equal(11.50m, Kpi(kidThisYear, "spend"));
        Assert.Equal([kid], kidThisYear.Plan.TagIds);
        // A tag question is about some items only: the whole-receipt coupon is left out, as for categories.
        Assert.Equal(0, kidThisYear.Result.Coverage.UnlinkedDiscountsExcluded);
        Assert.Equal(3, kidThisYear.Result.Coverage.PurchasesInScope);
        Assert.Equal(
            "Потрачено 11,50 zł: 2 позиции в 2 чеках. Скидки сэкономили 1,00 zł. Учтены только товары с тегом «Для ребёнка».",
            kidThisYear.Conclusion);
        var kidEver = await RunAsync(application, new AnalyticsPlan { TagIds = [kid] }, "en");
        Assert.Equal(15.49m, Kpi(kidEver, "spend"));
        Assert.EndsWith("Only products tagged «Для ребёнка» are counted.", kidEver.Conclusion, StringComparison.Ordinal);

        // «Акция» is on milk and coffee; the coffee bought with the receipt coupon cannot claim a share of it.
        var promoJuly = await RunAsync(application, new AnalyticsPlan { TagIds = [promo], From = new DateOnly(2026, 7, 1) });
        Assert.Equal(64.50m, Kpi(promoJuly, "spend"));
        Assert.Equal(1, promoJuly.Result.Coverage.UnlinkedDiscountsExcluded);

        // Tags narrow a category (AND), and several tags must all be present.
        var promoDairy = await RunAsync(application, new AnalyticsPlan { TagIds = [promo], CategoryIds = [Dairy], From = YearStart });
        Assert.Equal(11.50m, Kpi(promoDairy, "spend"));
        var kidCoffee = await RunAsync(application, new AnalyticsPlan { TagIds = [kid], CategoryIds = [Coffee] });
        Assert.False(kidCoffee.Result.HasData);
        Assert.Equal("По этим условиям покупок не нашлось. Учтены только товары с тегом «Для ребёнка».", kidCoffee.Conclusion);
        var both = await RunAsync(application, new AnalyticsPlan { TagIds = [kid, promo], From = YearStart });
        Assert.Equal(11.50m, Kpi(both, "spend"));
        Assert.EndsWith("Учтены только товары со всеми тегами «Для ребёнка», «Акция».", both.Conclusion, StringComparison.Ordinal);
        var none = await RunAsync(application, new AnalyticsPlan { TagIds = [kid, vegan] });
        Assert.False(none.Result.HasData);

        var lidlPromo = await RunAsync(application, new AnalyticsPlan { TagIds = [promo], Merchants = ["lidl"] });
        Assert.Equal(4.50m, Kpi(lidlPromo, "spend"));

        // An unknown tag is dropped and reported like an unknown category.
        var unknown = Guid.NewGuid();
        var cleaned = await RunAsync(application, new AnalyticsPlan { TagIds = [unknown, kid], From = YearStart });
        Assert.Equal([kid], cleaned.Plan.TagIds);
        Assert.Equal(new AnalyticsNote(AnalyticsNote.UnknownTag, unknown.ToString()), Assert.Single(cleaned.Notes));
        Assert.Equal(11.50m, Kpi(cleaned, "spend"));
    }

    [Fact]
    public async Task BreakdownByTagCountsALineUnderEachOfItsTags()
    {
        await using var application = await SeedAsync();
        var (kid, promo, vegan) = await TagAsync(application);

        // The vocabulary lists every tag, most used first: «Акция» (2 products), «Для ребёнка» (1), «Веган» (0).
        await using (var scope = application.Services.CreateAsyncScope())
        {
            var vocabulary = await scope.ServiceProvider.GetRequiredService<IAnalyticsRepository>().GetVocabularyAsync(Cancel);
            Assert.Equal([promo, kid, vegan], vocabulary.Tags.Select(tag => tag.Id));
        }

        var byTag = await RunAsync(application, new AnalyticsPlan { From = YearStart, To = Today, GroupBy = AnalyticsGroupBy.Tag });
        // Milk (7 + 4.50) is under both of its tags; cheese and chocolate have none; the coupon stays its own bar.
        Assert.Equal(
            [("Акция", promo, 121.50m, 4), (null, (Guid?)null, 25.00m, 2), ("Для ребёнка", kid, 11.50m, 2), (null, null, -5.00m, 0)],
            byTag.Result.Groups.Select(group => (group.Label, group.TagId, group.Value, group.LineCount)));
        Assert.Equal("", byTag.Result.Groups[1].Key);
        Assert.Equal("receiptDiscount", byTag.Result.Groups[3].CategorySystemKey);
        // The total and the coverage count each line once; only the bars overlap.
        Assert.Equal(141.50m, Kpi(byTag, "spend"));
        Assert.Equal(6, byTag.Result.Coverage.MatchedLines);
        Assert.Equal(2, byTag.Result.Coverage.MultiTaggedLines);
        Assert.True(byTag.Result.Groups.Sum(group => group.Value) > Kpi(byTag, "spend"));
        // The overlap is explained beside the breakdown (coverage.multiTaggedLines), not in the conclusion.
        Assert.Equal("Потрачено 141,50 zł: 6 позиций в 3 чеках. Скидки сэкономили 6,00 zł.", byTag.Conclusion);
        var english = await RunAsync(application, new AnalyticsPlan { From = YearStart, GroupBy = AnalyticsGroupBy.Tag }, "en");
        Assert.DoesNotContain("tag", english.Conclusion, StringComparison.OrdinalIgnoreCase);

        // Combined with a category and a tag filter; counts per tag are distinct receipts.
        var milkCount = await RunAsync(application, new AnalyticsPlan
        {
            Metric = AnalyticsMetric.PurchaseCount, CategoryIds = [Milk], TagIds = [promo], GroupBy = AnalyticsGroupBy.Tag
        });
        Assert.Equal([("Акция", 3m), ("Для ребёнка", 3m)], milkCount.Result.Groups.Select(group => (group.Label, group.Value)));

        // Single-tag lines only: no overlap, so no warning.
        var coffee = await RunAsync(application, new AnalyticsPlan { CategoryIds = [Coffee], GroupBy = AnalyticsGroupBy.Tag });
        Assert.Equal(0, coffee.Result.Coverage.MultiTaggedLines);
        Assert.DoesNotContain("тег", coffee.Conclusion, StringComparison.Ordinal);

        var top = await RunAsync(application, new AnalyticsPlan { Metric = AnalyticsMetric.Top, From = YearStart, GroupBy = AnalyticsGroupBy.Tag, Limit = 2 });
        Assert.Equal(AnalyticsGroupBy.Tag, top.Plan.GroupBy);
        Assert.Equal(2, top.Result.Groups.Count);
        Assert.StartsWith("Больше всего потрачено на «Акция»: 121,50", top.Conclusion, StringComparison.Ordinal);
    }

    [Fact]
    public void UntaggedLeaderIsNamedAsNoTag()
    {
        var purchase = new AnalyticsPurchaseFact(Guid.NewGuid(), null, new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero), true, "Lidl", "PLN",
        [
            new AnalyticsLineFact(0, "CHLEB", 1, null, 10m, 10m, false, null, null, null, null, null, null)
        ]);
        var result = AnalyticsCalculator.Calculate(
            new AnalyticsPlan { Metric = AnalyticsMetric.Top, GroupBy = AnalyticsGroupBy.Tag, Limit = 5 }, [purchase],
            new AnalyticsVocabulary([], ["Lidl"], []), TimeZoneInfo.Utc, false);
        var group = Assert.Single(result.Groups);
        Assert.Equal(("", (string?)null, 10m), (group.Key, group.Label, group.Value));
        Assert.StartsWith("Больше всего потрачено на «без тега»", AnalyticsAnswerText.Conclusion(result, "ru"), StringComparison.Ordinal);
        Assert.StartsWith("Most was spent on «no tag»", AnalyticsAnswerText.Conclusion(result, "en"), StringComparison.Ordinal);
    }

    /// <summary>Milk: «Для ребёнка» and «Акция»; coffee: «Акция»; «Веган» on nothing.</summary>
    internal static async Task<(Guid Kid, Guid Promo, Guid Vegan)> TagAsync(TestApplication application)
    {
        await using var scope = application.Services.CreateAsyncScope();
        var tags = scope.ServiceProvider.GetRequiredService<IProductTagService>();
        var kid = (await tags.CreateAsync("Для ребёнка", Cancel)).Id;
        var promo = (await tags.CreateAsync("Акция", Cancel)).Id;
        var vegan = (await tags.CreateAsync("Веган", Cancel)).Id;
        var context = scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>();
        var milk = (await context.Products.AsNoTracking().SingleAsync(product => product.NormalizedName.StartsWith("mleko"), Cancel)).Id;
        var coffee = (await context.Products.AsNoTracking().SingleAsync(product => product.NormalizedName.StartsWith("kawa"), Cancel)).Id;
        await tags.SetProductTagsAsync(milk, [kid, promo], Anna, Cancel);
        await tags.SetProductTagsAsync(coffee, [promo], Anna, Cancel);
        return (kid, promo, vegan);
    }

    internal static async Task<AnalyticsAnswer> RunAsync(TestApplication application, AnalyticsPlan plan, string locale = "ru")
    {
        await using var scope = application.Services.CreateAsyncScope();
        return await CreateService(scope, new ThrowingPlanner()).RunAsync(plan, locale, Cancel);
    }

    internal static AnalyticsService CreateService(AsyncServiceScope scope, IAnalyticsQueryPlanner planner) => new(
        scope.ServiceProvider.GetRequiredService<IAnalyticsRepository>(),
        planner,
        scope.ServiceProvider.GetRequiredService<TimeProvider>(),
        new AnalyticsSettings(TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw")),
        NullLogger<AnalyticsService>.Instance);

    private static decimal? Kpi(AnalyticsAnswer answer, string code) =>
        answer.Result.Kpis.SingleOrDefault(kpi => kpi.Code == code)?.Value;

    /// <summary>
    /// Four receipts: milk with a linked discount and coffee (June), milk and
    /// cheese by weight at Lidl (July), chocolate and coffee with a whole-receipt
    /// coupon (July), and one milk from last December.
    /// </summary>
    internal static async Task<TestApplication> SeedAsync()
    {
        var application = await TestApplication.CreateAsync();
        await SaveAsync(application, "Biedronka", new DateTimeOffset(2026, 6, 10, 10, 0, 0, TimeSpan.Zero),
            new PurchaseItemCommand("MLEKO LACIATE 3,2% 1L", 2, "szt", 4.00m, 8.00m),
            new PurchaseItemCommand("RABAT", null, null, null, -1.00m, ReceiptLineKind.Discount),
            new PurchaseItemCommand("KAWA LAVAZZA 1KG", 1, "szt", 50.00m, 50.00m));
        await SaveAsync(application, "Lidl", new DateTimeOffset(2026, 7, 2, 10, 0, 0, TimeSpan.Zero),
            new PurchaseItemCommand("MLEKO LACIATE 3,2% 1L", 1, "szt", 4.50m, 4.50m),
            new PurchaseItemCommand("SER GOUDA", 0.5m, "kg", 30.00m, 15.00m));
        await SaveAsync(application, "BIEDRONKA", new DateTimeOffset(2026, 7, 10, 10, 0, 0, TimeSpan.Zero),
            new PurchaseItemCommand("CZEKOLADA MILKA", 2, "szt", 5.00m, 10.00m),
            new PurchaseItemCommand("KAWA LAVAZZA 1KG", 1, "szt", 60.00m, 60.00m),
            new PurchaseItemCommand("RABAT NA PARAGON", null, null, null, -5.00m, ReceiptLineKind.Discount));
        await SaveAsync(application, "Biedronka", new DateTimeOffset(2025, 12, 20, 10, 0, 0, TimeSpan.Zero),
            new PurchaseItemCommand("MLEKO LACIATE 3,2% 1L", 1, "szt", 3.99m, 3.99m));

        await ClassifyAsync(application, "mleko", Milk, "Łaciate");
        await ClassifyAsync(application, "kawa", Coffee, "Lavazza");
        await ClassifyAsync(application, "ser", Cheese, null);
        return application;
    }

    private static async Task SaveAsync(TestApplication application, string merchant, DateTimeOffset purchasedAt, params PurchaseItemCommand[] items)
    {
        await using var scope = application.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IPurchaseDraftService>().CreateAsync(
            new CreatePurchaseDraftCommand(merchant, purchasedAt, "PLN", null, items, AllowPossibleDuplicate: true),
            Cancel);
    }

    private static async Task ClassifyAsync(TestApplication application, string namePrefix, Guid categoryId, string? brand)
    {
        await using var scope = application.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>();
        var product = await context.Products.AsNoTracking().SingleAsync(entry => entry.NormalizedName.StartsWith(namePrefix), Cancel);
        await scope.ServiceProvider.GetRequiredService<IProductCategoryService>().ClassifyProductAsync(
            new ClassifyProductCommand(product.Id, true, categoryId, brand is not null, brand), Anna, Cancel);
    }

    private sealed class ThrowingPlanner : IAnalyticsQueryPlanner
    {
        public bool IsConfigured => false;

        public string ModelName => "none";

        public Task<AnalyticsPlanningResult> PlanAsync(AnalyticsPlanningRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Running a plan must never call the planner.");
    }
}
