using PurchaseMemory.Application.Products;

namespace PurchaseMemory.IntegrationTests;

/// <summary>
/// The arithmetic behind «how much did this cost last time», exercised without a
/// database so each rule can be stated on its own.
/// </summary>
public sealed class ProductPriceHistoryTests
{
    private static readonly Guid FirstPurchase = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SecondPurchase = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ThirdPurchase = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static readonly DateTimeOffset July1 = new(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset July11 = new(2026, 7, 11, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset July21 = new(2026, 7, 21, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ASinglePurchaseStillReportsAPriceAndRefusesToInventARhythm()
    {
        var summary = ProductPriceHistoryCalculator.Summarize(
            [Fact_(FirstPurchase, July1, "Biedronka", unitPrice: 4.99m)]);

        var series = Assert.Single(summary.Series);
        Assert.Equal(4.99m, series.MinimumPrice);
        Assert.Equal(4.99m, series.AveragePrice);
        Assert.Equal(4.99m, series.LatestPrice);
        Assert.Equal("Biedronka", series.LatestPoint.MerchantName);
        Assert.Single(series.Points);

        var frequency = summary.Frequency;
        Assert.NotNull(frequency);
        Assert.Equal(1, frequency.PurchaseCount);
        Assert.Equal(July1, frequency.FirstPurchasedAt);
        Assert.Equal(July1, frequency.LastPurchasedAt);
        Assert.Null(frequency.AverageDaysBetweenPurchases);
    }

    [Fact]
    public void PricesFromDifferentStoresShareOneSeriesAndTheLatestOneWins()
    {
        var summary = ProductPriceHistoryCalculator.Summarize(
        [
            Fact_(FirstPurchase, July1, "Biedronka", unitPrice: 4.99m),
            Fact_(SecondPurchase, July11, "Lidl", unitPrice: 3.49m),
            Fact_(ThirdPurchase, July21, "Żabka", unitPrice: 6.30m)
        ]);

        var series = Assert.Single(summary.Series);
        Assert.Equal(["Biedronka", "Lidl", "Żabka"], series.Points.Select(point => point.MerchantName));
        Assert.Equal(3.49m, series.MinimumPrice);
        Assert.Equal(4.93m, series.AveragePrice);
        Assert.Equal(6.30m, series.LatestPrice);
        Assert.Equal("Żabka", series.LatestPoint.MerchantName);

        var frequency = summary.Frequency;
        Assert.NotNull(frequency);
        Assert.Equal(3, frequency.PurchaseCount);
        Assert.Equal(10d, frequency.AverageDaysBetweenPurchases!.Value, precision: 6);
    }

    [Fact]
    public void PiecesAndKilogramsStayInSeparateSeriesInsteadOfAveragingIntoNonsense()
    {
        var summary = ProductPriceHistoryCalculator.Summarize(
        [
            Fact_(FirstPurchase, July1, "Biedronka", unitPrice: 4.99m, unit: "szt"),
            Fact_(SecondPurchase, July11, "Lidl", unitPrice: 24.90m, unit: "kg"),
            Fact_(ThirdPurchase, July21, "Lidl", unitPrice: 22.50m, unit: "KG")
        ]);

        Assert.Equal(2, summary.Series.Count);

        // The better-evidenced unit leads, whichever it happens to be.
        var kilograms = summary.Series[0];
        Assert.Equal("kg", kilograms.Unit);
        Assert.Equal(2, kilograms.Points.Count);
        Assert.Equal(22.50m, kilograms.LatestPrice);
        Assert.Equal(23.70m, kilograms.AveragePrice);

        var pieces = summary.Series[1];
        Assert.Equal("szt", pieces.Unit);
        Assert.Equal(4.99m, Assert.Single(pieces.Points).UnitPrice);
    }

    [Fact]
    public void GramsAreNotFoldedOntoKilogramsBecauseThatWouldMultiplyAnOcrGuessByAThousand()
    {
        var summary = ProductPriceHistoryCalculator.Summarize(
        [
            Fact_(FirstPurchase, July1, "Biedronka", unitPrice: 24.90m, unit: "kg"),
            Fact_(SecondPurchase, July11, "Biedronka", unitPrice: 0.03m, unit: "g")
        ]);

        Assert.Equal(["g", "kg"], summary.Series.Select(series => series.Unit).Order());
    }

    [Fact]
    public void ALineWithoutAUnitIsTreatedAsASinglePieceRatherThanItsOwnBucket()
    {
        var summary = ProductPriceHistoryCalculator.Summarize(
        [
            Fact_(FirstPurchase, July1, "Biedronka", unitPrice: 4.99m, unit: null),
            Fact_(SecondPurchase, July11, "Biedronka", unitPrice: 5.49m, unit: "SZT."),
            Fact_(ThirdPurchase, July21, "Biedronka", unitPrice: 5.99m, unit: "шт")
        ]);

        var series = Assert.Single(summary.Series);
        Assert.Equal(ProductUnit.Piece, series.Unit);
        Assert.Equal(3, series.Points.Count);
    }

    [Fact]
    public void DifferentCurrenciesNeverShareASeries()
    {
        var summary = ProductPriceHistoryCalculator.Summarize(
        [
            Fact_(FirstPurchase, July1, "Biedronka", unitPrice: 4.99m),
            Fact_(SecondPurchase, July11, "Rewe", unitPrice: 1.19m, currency: "eur")
        ]);

        Assert.Equal(["EUR", "PLN"], summary.Series.Select(series => series.Currency).Order());
    }

    [Fact]
    public void AMissingUnitPriceIsDerivedFromTheLineTotalAndQuantity()
    {
        var summary = ProductPriceHistoryCalculator.Summarize(
        [
            Fact_(FirstPurchase, July1, "Biedronka", unitPrice: null, lineTotal: 14.97m, quantity: 3m),
            // No quantity on the receipt means one item, so the line total is the price.
            Fact_(SecondPurchase, July11, "Biedronka", unitPrice: null, lineTotal: 5.20m, quantity: null)
        ]);

        var series = Assert.Single(summary.Series);
        Assert.Equal([4.99m, 5.20m], series.Points.Select(point => point.UnitPrice));
    }

    [Fact]
    public void AQuantityOfZeroOrLessNeverDividesThePriceIntoNonsense()
    {
        // The domain rejects these quantities on the way in, so they can only
        // reach the calculator from older rows or a future import path. Dividing
        // by them would throw or invert the price, so the line is dropped.
        var summary = ProductPriceHistoryCalculator.Summarize(
        [
            Fact_(FirstPurchase, July1, "Biedronka", unitPrice: null, lineTotal: 9.99m, quantity: 0m),
            Fact_(SecondPurchase, July11, "Biedronka", unitPrice: null, lineTotal: 9.99m, quantity: -2m)
        ]);

        Assert.Empty(summary.Series);
        Assert.Equal(2, summary.UnpricedItemCount);
    }

    [Fact]
    public void WhenTheUnitPriceAndTheLineTotalDisagreeTheUnitPriceWins()
    {
        // A mismatch between the two is exactly what the receipt-review issue
        // detector already flags. Until a person resolves it, the printed unit
        // price is the more direct statement of what one unit cost.
        var summary = ProductPriceHistoryCalculator.Summarize(
            [Fact_(FirstPurchase, July1, "Biedronka", unitPrice: 4.99m, lineTotal: 99.00m, quantity: 2m)]);

        var series = Assert.Single(summary.Series);
        Assert.Equal(4.99m, Assert.Single(series.Points).UnitPrice);
    }

    [Fact]
    public void LinesWithNoUsablePriceAreCountedAsideInsteadOfScoringZero()
    {
        var summary = ProductPriceHistoryCalculator.Summarize(
        [
            Fact_(FirstPurchase, July1, "Biedronka", unitPrice: 4.99m),
            Fact_(SecondPurchase, July11, "Biedronka", unitPrice: null, lineTotal: null)
        ]);

        var series = Assert.Single(summary.Series);
        Assert.Equal(4.99m, series.MinimumPrice);
        Assert.Equal(1, summary.UnpricedItemCount);

        // The purchase still happened, so it counts towards how often we buy this.
        Assert.NotNull(summary.Frequency);
        Assert.Equal(2, summary.Frequency.PurchaseCount);
    }

    [Fact]
    public void TheAverageWeighsEachPurchaseOnceEvenWhenAReceiptHasSeveralLines()
    {
        // One receipt with two lines at 3.00 and 5.00, another with one at 9.00.
        // Per line the average would be 5.67 and the first trip would count
        // twice; per purchase it is (4.00 + 9.00) / 2 = 6.50, which is what the
        // «2 покупки» printed above the figure promises.
        var summary = ProductPriceHistoryCalculator.Summarize(
        [
            Fact_(FirstPurchase, July1, "Biedronka", unitPrice: 3.00m),
            Fact_(FirstPurchase, July1, "Biedronka", unitPrice: 5.00m),
            Fact_(SecondPurchase, July11, "Lidl", unitPrice: 9.00m)
        ]);

        var series = Assert.Single(summary.Series);
        Assert.Equal(6.50m, series.AveragePrice);

        // The extremes stay per observation: 3.00 really was paid once.
        Assert.Equal(3.00m, series.MinimumPrice);
        Assert.Equal(3, series.Points.Count);

        var frequency = summary.Frequency;
        Assert.NotNull(frequency);
        Assert.Equal(2, frequency.PurchaseCount);
    }

    [Fact]
    public void TwoLinesOnOneReceiptCountAsOneShoppingTrip()
    {
        var summary = ProductPriceHistoryCalculator.Summarize(
        [
            Fact_(FirstPurchase, July1, "Biedronka", unitPrice: 4.99m),
            Fact_(FirstPurchase, July1, "Biedronka", unitPrice: 4.99m)
        ]);

        var frequency = summary.Frequency;
        Assert.NotNull(frequency);
        Assert.Equal(1, frequency.PurchaseCount);
        Assert.Null(frequency.AverageDaysBetweenPurchases);
        Assert.Equal(2, Assert.Single(summary.Series).Points.Count);
    }

    [Fact]
    public void NoPurchasesAtAllProduceNoSeriesAndNoFrequency()
    {
        var summary = ProductPriceHistoryCalculator.Summarize([]);

        Assert.Empty(summary.Series);
        Assert.Equal(0, summary.UnpricedItemCount);
        Assert.Null(summary.Frequency);
    }

    private static ProductPurchaseFact Fact_(
        Guid purchaseId,
        DateTimeOffset occurredAt,
        string? merchantName,
        decimal? unitPrice,
        decimal? lineTotal = null,
        decimal? quantity = null,
        string? unit = "szt",
        string? currency = "PLN")
    {
        return new ProductPurchaseFact(
            purchaseId,
            occurredAt,
            merchantName,
            currency,
            "Mleko Łaciate 3,2% 1L",
            quantity,
            unit,
            unitPrice,
            lineTotal ?? unitPrice);
    }
}
