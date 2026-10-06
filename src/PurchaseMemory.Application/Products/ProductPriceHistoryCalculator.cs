namespace PurchaseMemory.Application.Products;

/// <summary>
/// Turns the raw purchased lines of one product into the numbers the product
/// page answers with: what it cost last time, what it usually costs, and how the
/// price moved. Pure arithmetic over the facts it is handed, so the rules below
/// can be read and tested without a database.
/// </summary>
public static class ProductPriceHistoryCalculator
{
    /// <summary>
    /// Money is rounded to the smallest unit of the currency. Averages of
    /// recognized prices otherwise carry digits the receipt never printed.
    /// </summary>
    private const int MoneyDecimals = 2;

    public static ProductPriceSummary Summarize(IEnumerable<ProductPurchaseFact> facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        var materialized = facts.ToList();
        var unpricedItemCount = 0;
        var priced = new List<(string? Currency, string Unit, ProductPricePoint Point)>();

        foreach (var fact in materialized)
        {
            var unitPrice = ResolveUnitPrice(fact);
            if (unitPrice is null)
            {
                unpricedItemCount++;
                continue;
            }

            priced.Add((
                NormalizeCurrency(fact.Currency),
                ProductUnit.Canonicalize(fact.Unit),
                new ProductPricePoint(
                    fact.OccurredAt,
                    fact.MerchantName,
                    decimal.Round(unitPrice.Value, MoneyDecimals, MidpointRounding.ToEven),
                    fact.PurchaseId)));
        }

        var series = priced
            .GroupBy(entry => (entry.Currency, entry.Unit))
            .Select(group => BuildSeries(group.Key.Currency, group.Key.Unit, group.Select(entry => entry.Point)))
            // The page leads with the best-evidenced series and keeps the rest
            // beside it. Recency breaks a tie, because the unit a product is sold
            // in today is the one the reader is about to compare against.
            .OrderByDescending(entry => entry.Points.Count)
            .ThenByDescending(entry => entry.LatestPoint.PurchasedAt)
            .ThenBy(entry => entry.Unit, StringComparer.Ordinal)
            .ToList();

        return new ProductPriceSummary(
            series,
            unpricedItemCount,
            BuildFrequency(materialized));
    }

    /// <summary>
    /// What one unit cost on this line.
    ///
    /// A receipt states the unit price directly, or states a line total and a
    /// quantity to divide it by. A line with neither — an unreadable price, or a
    /// deposit line recognition kept — contributes no point at all rather than a
    /// zero that would drag the minimum down.
    ///
    /// A missing quantity is read as one: a Polish receipt prints the quantity
    /// only when it is not one, so the line total <i>is</i> the unit price.
    /// </summary>
    private static decimal? ResolveUnitPrice(ProductPurchaseFact fact)
    {
        if (fact.UnitPrice is > 0)
        {
            return fact.UnitPrice;
        }

        if (fact.LineTotal is not > 0)
        {
            return null;
        }

        var quantity = fact.Quantity ?? 1m;
        return quantity > 0 ? fact.LineTotal.Value / quantity : null;
    }

    private static ProductPriceSeries BuildSeries(
        string? currency,
        string unit,
        IEnumerable<ProductPricePoint> points)
    {
        var ordered = points
            .OrderBy(point => point.PurchasedAt)
            .ThenBy(point => point.PurchaseId)
            .ToList();

        // Averaged per purchase, not per line, and unweighted by quantity.
        //
        // Per purchase, because this page has already settled that two jars of
        // the same yoghurt on one receipt are one purchase: letting that receipt
        // weigh twice here would contradict the count printed right above it.
        // Lines of one purchase are averaged first, then those values are.
        //
        // Unweighted, because the question is «what does this usually cost per
        // unit», not «what did we pay per unit across all the weight we bought»:
        // weighting by quantity would let one bulk purchase decide the number.
        var perPurchase = ordered
            .GroupBy(point => point.PurchaseId)
            .Select(group => group.Sum(point => point.UnitPrice) / group.Count())
            .ToList();

        var average = decimal.Round(
            perPurchase.Sum() / perPurchase.Count,
            MoneyDecimals,
            MidpointRounding.ToEven);

        return new ProductPriceSeries(
            currency,
            unit,
            ordered,
            ordered.Min(point => point.UnitPrice),
            average,
            ordered[^1]);
    }

    /// <summary>
    /// Counts distinct purchases rather than lines: buying the same yoghurt twice
    /// on one receipt is one shopping trip, not two, and the gap between trips is
    /// what «how often do we buy this» means.
    /// </summary>
    private static ProductPurchaseFrequency? BuildFrequency(List<ProductPurchaseFact> facts)
    {
        if (facts.Count == 0)
        {
            return null;
        }

        var purchaseMoments = facts
            .GroupBy(fact => fact.PurchaseId)
            .Select(group => group.Min(fact => fact.OccurredAt))
            .OrderBy(moment => moment)
            .ToList();

        var first = purchaseMoments[0];
        var last = purchaseMoments[^1];

        // One purchase has no interval, and several purchases made on the same
        // day would report «every 0 days», which reads as a fact but is an
        // artefact of a single shopping trip split across receipts.
        var span = (last - first).TotalDays;
        double? averageDays = purchaseMoments.Count > 1 && span > 0
            ? span / (purchaseMoments.Count - 1)
            : null;

        return new ProductPurchaseFrequency(first, last, purchaseMoments.Count, averageDays);
    }

    private static string? NormalizeCurrency(string? currency)
    {
        return string.IsNullOrWhiteSpace(currency) ? null : currency.Trim().ToUpperInvariant();
    }
}

/// <summary>Everything <see cref="ProductPriceHistoryCalculator"/> derives from one product's lines.</summary>
public sealed record ProductPriceSummary(
    IReadOnlyList<ProductPriceSeries> Series,
    int UnpricedItemCount,
    ProductPurchaseFrequency? Frequency);
