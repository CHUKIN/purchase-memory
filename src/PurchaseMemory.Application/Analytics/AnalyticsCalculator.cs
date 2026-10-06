using System.Globalization;
using PurchaseMemory.Application.Products;

namespace PurchaseMemory.Application.Analytics;

/// <summary>
/// Executes a validated plan over purchases loaded from the database. It is a
/// pure function: the same purchases and plan always give the same numbers.
/// <para>
/// Aggregation happens here in <see cref="decimal"/> rather than in SQL:
/// SQLite cannot sum decimals, a household has thousands of lines rather than
/// millions, and discount lines must be attached to their product line before
/// anything is added up — which is receipt logic, not a GROUP BY.
/// </para>
/// </summary>
public static class AnalyticsCalculator
{
    public const string FallbackCurrency = "PLN";
    public const int MaximumGroups = 12;
    public const int MaximumEvidence = 30;
    public const int MaximumEvidenceItems = 6;
    private const int MaximumTimeBuckets = 60;

    public static AnalyticsResult Calculate(
        AnalyticsPlan plan,
        IReadOnlyList<AnalyticsPurchaseFact> purchases,
        AnalyticsVocabulary vocabulary,
        TimeZoneInfo timeZone,
        bool truncated)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(purchases);
        ArgumentNullException.ThrowIfNull(vocabulary);
        ArgumentNullException.ThrowIfNull(timeZone);

        var matcher = new LineMatcher(plan, vocabulary);
        var merchantKeys = plan.Merchants.Select(ProductNameNormalizer.Normalize).ToHashSet(StringComparer.Ordinal);

        var inScope = purchases
            .Where(purchase => InPeriod(purchase, plan, timeZone))
            .Where(purchase => merchantKeys.Count == 0
                || merchantKeys.Contains(ProductNameNormalizer.Normalize(purchase.MerchantName)))
            .ToList();

        var matched = new List<MatchedLine>();
        int uncategorized = 0, unlinked = 0, unlinkedDiscounts = 0;
        foreach (var purchase in inScope)
        {
            // A discount pointing at a position that is not a product line on this
            // receipt belongs to no product: it is treated as a whole-receipt discount
            // rather than silently dropped.
            var productPositions = purchase.Lines.Where(line => !line.IsDiscount).Select(line => line.Position).ToHashSet();
            bool Linked(AnalyticsLineFact line) =>
                line.DiscountedItemPosition is { } target && productPositions.Contains(target);
            var discountsByTarget = purchase.Lines
                .Where(line => line.IsDiscount && Linked(line))
                .GroupBy(line => line.DiscountedItemPosition!.Value)
                .ToDictionary(group => group.Key, group => group.Sum(line => line.LineTotal ?? 0m));
            var receiptDiscounts = purchase.Lines
                .Where(line => line.IsDiscount && !Linked(line))
                .ToList();

            var matchedHere = 0;
            foreach (var line in purchase.Lines.Where(line => !line.IsDiscount))
            {
                if (line.ProductId is null)
                {
                    unlinked++;
                }
                else if (line.CategoryId is null)
                {
                    uncategorized++;
                }

                var discount = discountsByTarget.GetValueOrDefault(line.Position);
                if (!matcher.Matches(line, discount != 0m))
                {
                    continue;
                }

                matchedHere++;
                matched.Add(new MatchedLine(purchase, line, discount, LocalDay(purchase.PurchasedAt, timeZone)));
            }

            if (matchedHere == 0 || receiptDiscounts.Count == 0)
            {
                continue;
            }

            if (plan.HasLineFilter)
            {
                // «Сколько стоило молоко» cannot claim a share of a whole-receipt coupon.
                unlinkedDiscounts += receiptDiscounts.Count;
            }
            else
            {
                // Whole-receipt questions keep receipt-level discounts: they were really not paid.
                var anchor = matched[^1];
                foreach (var receiptDiscount in receiptDiscounts)
                {
                    matched.Add(new MatchedLine(purchase, receiptDiscount, 0m, anchor.Day) { IsReceiptDiscount = true });
                }
            }
        }

        var currency = matched
            .GroupBy(line => CurrencyOf(line.Purchase))
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key == FallbackCurrency ? 0 : 1)
            .Select(group => group.Key)
            .FirstOrDefault() ?? FallbackCurrency;
        var otherCurrency = matched.Count(line => !line.IsReceiptDiscount && CurrencyOf(line.Purchase) != currency);
        var money = matched.Where(line => CurrencyOf(line.Purchase) == currency).ToList();
        var products = matched.Where(line => !line.IsReceiptDiscount).ToList();
        var productLinesInCurrency = money.Where(line => !line.IsReceiptDiscount).ToList();

        var quantityUnit = products
            .GroupBy(line => QuantityUnit(line.Line.Unit).Unit)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => group.Key)
            .FirstOrDefault();
        var priceUnit = productLinesInCurrency
            .Where(line => UnitPrice(line.Line) is not null)
            .GroupBy(line => ProductUnit.Canonicalize(line.Line.Unit))
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => group.Key)
            .FirstOrDefault();

        // Lines with no printed quantity count as one piece; coverage says how many.
        var assumedQuantity = plan.Metric == AnalyticsMetric.Quantity
            ? products.Count(line => line.Line.Quantity is null)
            : 0;

        var otherUnit = plan.Metric switch
        {
            AnalyticsMetric.Quantity => products.Count(line => QuantityUnit(line.Line.Unit).Unit != quantityUnit),
            AnalyticsMetric.AverageUnitPrice or AnalyticsMetric.MinimumUnitPrice or AnalyticsMetric.MaximumUnitPrice =>
                productLinesInCurrency.Count(line => UnitPrice(line.Line) is not null && ProductUnit.Canonicalize(line.Line.Unit) != priceUnit),
            _ => 0
        };

        var context = new Context(plan, currency, quantityUnit, priceUnit);
        var kpis = BuildKpis(context, money, products);
        var (groups, hidden) = BuildGroups(context, money, products, vocabulary);
        var matchedPurchases = products.Select(line => line.Purchase.PurchaseId).Distinct().Count();

        var evidence = money
            .GroupBy(line => line.Purchase.PurchaseId)
            .Select(group =>
            {
                var purchase = group.First().Purchase;
                var lines = group.Where(line => !line.IsReceiptDiscount).ToList();
                return new AnalyticsEvidencePurchase(
                    purchase.PurchaseId,
                    purchase.SourceReceiptId,
                    purchase.PurchasedAt,
                    purchase.MerchantName,
                    currency,
                    Round(group.Sum(line => line.Paid)),
                    lines.Count,
                    lines
                        .Take(MaximumEvidenceItems)
                        .Select(line => new AnalyticsEvidenceItem(line.Line.Name, line.Line.ProductId, line.Line.LineTotal, line.Discount))
                        .ToList());
            })
            .Where(purchase => purchase.MatchedLineCount > 0)
            .OrderByDescending(purchase => purchase.PurchasedAt)
            .ThenBy(purchase => purchase.PurchaseId)
            .ToList();

        var coverage = new AnalyticsCoverage(
            inScope.Count,
            matchedPurchases,
            products.Count,
            uncategorized,
            unlinked,
            products.Count(line => line.Line.LineTotal is null),
            unlinkedDiscounts,
            otherCurrency,
            otherUnit,
            inScope.Count(purchase => !purchase.HasReceiptDate),
            truncated,
            assumedQuantity,
            products.Count(line => line.Line.TagIds.Count > 1));

        return new AnalyticsResult(
            plan.Metric,
            plan.GroupBy,
            currency,
            products.Count > 0,
            kpis,
            groups,
            hidden,
            evidence.Take(MaximumEvidence).ToList(),
            evidence.Count,
            coverage);
    }

    private static List<AnalyticsKpi> BuildKpis(Context context, List<MatchedLine> money, List<MatchedLine> products)
    {
        var purchaseCount = products.Select(line => line.Purchase.PurchaseId).Distinct().Count();
        var kpis = new List<AnalyticsKpi>();
        switch (context.Plan.Metric)
        {
            case AnalyticsMetric.Spend:
            case AnalyticsMetric.Top:
                kpis.Add(new AnalyticsKpi("spend", AnalyticsKpiKind.Money, Round(money.Sum(line => line.Paid)), context.Currency));
                var discounts = money.Sum(line => line.IsReceiptDiscount ? line.Line.LineTotal ?? 0m : line.Discount);
                if (discounts != 0m)
                {
                    kpis.Add(new AnalyticsKpi("discounts", AnalyticsKpiKind.Money, Round(discounts), context.Currency));
                }

                kpis.Add(new AnalyticsKpi("purchaseCount", AnalyticsKpiKind.Count, purchaseCount));
                kpis.Add(new AnalyticsKpi("lineCount", AnalyticsKpiKind.Count, products.Count));
                break;
            case AnalyticsMetric.Quantity:
                if (context.QuantityUnit is not null)
                {
                    kpis.Add(new AnalyticsKpi("quantity", AnalyticsKpiKind.Quantity, Quantity(products, context.QuantityUnit), context.QuantityUnit));
                }

                kpis.Add(new AnalyticsKpi("lineCount", AnalyticsKpiKind.Count, products.Count));
                kpis.Add(new AnalyticsKpi("purchaseCount", AnalyticsKpiKind.Count, purchaseCount));
                break;
            case AnalyticsMetric.PurchaseCount:
                kpis.Add(new AnalyticsKpi("purchaseCount", AnalyticsKpiKind.Count, purchaseCount));
                kpis.Add(new AnalyticsKpi("lineCount", AnalyticsKpiKind.Count, products.Count));
                break;
            case AnalyticsMetric.AverageUnitPrice:
            case AnalyticsMetric.MinimumUnitPrice:
            case AnalyticsMetric.MaximumUnitPrice:
                var prices = Prices(money, context.PriceUnit);
                var code = context.Plan.Metric switch
                {
                    AnalyticsMetric.AverageUnitPrice => "averagePrice",
                    AnalyticsMetric.MinimumUnitPrice => "minimumPrice",
                    _ => "maximumPrice"
                };
                kpis.Add(new AnalyticsKpi(code, AnalyticsKpiKind.Money, Price(context.Plan.Metric, prices), context.Currency, null));
                if (context.Plan.Metric == AnalyticsMetric.AverageUnitPrice && prices.Count > 0)
                {
                    kpis.Add(new AnalyticsKpi("minimumPrice", AnalyticsKpiKind.Money, prices.Min(), context.Currency));
                    kpis.Add(new AnalyticsKpi("maximumPrice", AnalyticsKpiKind.Money, prices.Max(), context.Currency));
                }

                kpis.Add(new AnalyticsKpi("priceCount", AnalyticsKpiKind.Count, prices.Count, context.PriceUnit));
                break;
            case AnalyticsMetric.Exists:
                kpis.Add(new AnalyticsKpi("exists", AnalyticsKpiKind.Boolean, products.Count > 0 ? 1 : 0));
                kpis.Add(new AnalyticsKpi("purchaseCount", AnalyticsKpiKind.Count, purchaseCount));
                if (products.Count > 0)
                {
                    kpis.Add(new AnalyticsKpi("lastPurchase", AnalyticsKpiKind.Date, null, null, products.Max(line => line.Day)));
                }

                break;
        }

        return kpis;
    }

    private static (List<AnalyticsGroup> Groups, int Hidden) BuildGroups(
        Context context,
        List<MatchedLine> money,
        List<MatchedLine> products,
        AnalyticsVocabulary vocabulary)
    {
        var plan = context.Plan;
        if (plan.GroupBy == AnalyticsGroupBy.None || products.Count == 0)
        {
            return ([], 0);
        }

        // Money metrics read lines in the answer's currency; counts read every product line.
        var source = plan.Metric is AnalyticsMetric.Quantity or AnalyticsMetric.PurchaseCount ? products : money;
        var categories = vocabulary.Categories.ToDictionary(category => category.Id);
        var tags = vocabulary.Tags.ToDictionary(tag => tag.Id);
        // One spelling per store: «BIEDRONKA» and «Biedronka» are one bar, named as the data usually spells it.
        var merchantNames = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in vocabulary.Merchants)
        {
            merchantNames.TryAdd(ProductNameNormalizer.Normalize(name), name);
        }

        // Only tags put one line into several buckets; every other grouping gives a line exactly one.
        var buckets = source
            .SelectMany(line => BucketsOf(plan.GroupBy, line).Select(key => (Key: key, Line: line)))
            .GroupBy(entry => entry.Key, entry => entry.Line)
            .Select(group =>
            {
                var sample = group.First(line => !line.IsReceiptDiscount || group.All(entry => entry.IsReceiptDiscount));
                if (plan.GroupBy == AnalyticsGroupBy.Tag)
                {
                    return TagGroup(context, group.Key, group.ToList(), sample, tags);
                }

                var (label, categoryId, systemKey, productId) = LabelOf(plan.GroupBy, group.Key, sample, categories, merchantNames);
                return new AnalyticsGroup(
                    group.Key,
                    label,
                    GroupValue(context, group.ToList()),
                    group.Count(line => !line.IsReceiptDiscount),
                    categoryId,
                    systemKey,
                    productId);
            })
            .ToList();

        if (plan.GroupBy is AnalyticsGroupBy.Month or AnalyticsGroupBy.Week)
        {
            return (FillTimeGaps(plan, buckets), 0);
        }

        var ordered = buckets
            .OrderByDescending(group => group.Value)
            .ThenBy(group => group.Label ?? "\uffff", StringComparer.CurrentCulture)
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .ToList();
        var take = plan.Metric == AnalyticsMetric.Top ? plan.Limit ?? AnalyticsPlan.DefaultTopLimit : MaximumGroups;
        return (ordered.Take(take).ToList(), Math.Max(0, ordered.Count - take));
    }

    /// <summary>A tag bar: the tag's name and id, «no tag» (empty key, no label) or the receipt-discount bar.</summary>
    private static AnalyticsGroup TagGroup(
        Context context,
        string key,
        List<MatchedLine> lines,
        MatchedLine sample,
        Dictionary<Guid, AnalyticsTag> tags)
    {
        var value = GroupValue(context, lines);
        var lineCount = lines.Count(line => !line.IsReceiptDiscount);
        if (key == "receipt" && sample.IsReceiptDiscount)
        {
            return new AnalyticsGroup(key, null, value, lineCount, CategorySystemKey: "receiptDiscount");
        }

        return Guid.TryParse(key, out var tagId) && tags.TryGetValue(tagId, out var tag)
            ? new AnalyticsGroup(key, tag.Name, value, lineCount, TagId: tag.Id)
            : new AnalyticsGroup(key, null, value, lineCount);
    }

    private static decimal GroupValue(Context context, List<MatchedLine> lines)
    {
        var products = lines.Where(line => !line.IsReceiptDiscount).ToList();
        return context.Plan.Metric switch
        {
            AnalyticsMetric.Quantity => context.QuantityUnit is null ? products.Count : Quantity(products, context.QuantityUnit),
            AnalyticsMetric.PurchaseCount => products.Select(line => line.Purchase.PurchaseId).Distinct().Count(),
            AnalyticsMetric.AverageUnitPrice or AnalyticsMetric.MinimumUnitPrice or AnalyticsMetric.MaximumUnitPrice =>
                Price(context.Plan.Metric, Prices(lines, context.PriceUnit)) ?? 0m,
            AnalyticsMetric.Exists => products.Count,
            _ => Round(lines.Sum(line => line.Paid))
        };
    }

    private static List<AnalyticsGroup> FillTimeGaps(AnalyticsPlan plan, List<AnalyticsGroup> buckets)
    {
        var weekly = plan.GroupBy == AnalyticsGroupBy.Week;
        var existing = buckets.ToDictionary(group => group.Key, StringComparer.Ordinal);
        var keys = buckets.Select(group => group.Key).Order(StringComparer.Ordinal).ToList();
        var first = ParseBucket(plan.From is { } from ? BucketKey(weekly, from) : keys[0], weekly);
        var last = ParseBucket(plan.To is { } to ? BucketKey(weekly, to) : keys[^1], weekly);
        var dataFirst = ParseBucket(keys[0], weekly);
        var dataLast = ParseBucket(keys[^1], weekly);
        if (dataFirst < first) first = dataFirst;
        if (dataLast > last) last = dataLast;

        var filled = new List<AnalyticsGroup>();
        for (var cursor = first; cursor <= last && filled.Count < MaximumTimeBuckets; cursor = weekly ? cursor.AddDays(7) : cursor.AddMonths(1))
        {
            var key = BucketKey(weekly, cursor);
            filled.Add(existing.TryGetValue(key, out var group) ? group : new AnalyticsGroup(key, null, 0m, 0));
        }

        // A very long range keeps its most recent buckets.
        if (filled.Count == MaximumTimeBuckets && filled[^1].Key != BucketKey(weekly, last))
        {
            filled = [];
            for (var cursor = last; cursor >= first && filled.Count < MaximumTimeBuckets; cursor = weekly ? cursor.AddDays(-7) : cursor.AddMonths(-1))
            {
                var key = BucketKey(weekly, cursor);
                filled.Insert(0, existing.TryGetValue(key, out var group) ? group : new AnalyticsGroup(key, null, 0m, 0));
            }
        }

        return filled;
    }

    private static IEnumerable<string> BucketsOf(AnalyticsGroupBy groupBy, MatchedLine line)
    {
        if (groupBy != AnalyticsGroupBy.Tag)
        {
            return [BucketOf(groupBy, line)];
        }

        if (line.IsReceiptDiscount)
        {
            return ["receipt"];
        }

        // An untagged (or unlinked) line still counts — in the «no tag» bar.
        return line.Line.TagIds.Count == 0
            ? [""]
            : line.Line.TagIds.Distinct().Select(tagId => tagId.ToString());
    }

    private static string BucketOf(AnalyticsGroupBy groupBy, MatchedLine line) => groupBy switch
    {
        AnalyticsGroupBy.Month => BucketKey(false, line.Day),
        AnalyticsGroupBy.Week => BucketKey(true, line.Day),
        AnalyticsGroupBy.Merchant => ProductNameNormalizer.Normalize(line.Purchase.MerchantName),
        AnalyticsGroupBy.Category => line.IsReceiptDiscount ? "receipt" : line.Line.CategoryId?.ToString() ?? "",
        AnalyticsGroupBy.Product => line.IsReceiptDiscount
            ? "receipt"
            : line.Line.ProductId?.ToString() ?? "line:" + ProductNameNormalizer.Normalize(line.Line.Name),
        AnalyticsGroupBy.Brand => line.IsReceiptDiscount ? "receipt" : ProductNameNormalizer.Normalize(line.Line.Brand),
        _ => ""
    };

    private static (string? Label, Guid? CategoryId, string? SystemKey, Guid? ProductId) LabelOf(
        AnalyticsGroupBy groupBy,
        string key,
        MatchedLine sample,
        Dictionary<Guid, AnalyticsCategory> categories,
        Dictionary<string, string> merchantNames)
    {
        if (key == "receipt" && sample.IsReceiptDiscount)
        {
            return (null, null, "receiptDiscount", null);
        }

        return groupBy switch
        {
            AnalyticsGroupBy.Merchant => (merchantNames.TryGetValue(key, out var merchant) ? merchant : sample.Purchase.MerchantName, null, null, null),
            AnalyticsGroupBy.Category => sample.Line.CategoryId is { } id && categories.TryGetValue(id, out var category)
                ? (category.Name, category.Id, category.SystemKey, null)
                : (null, null, null, null),
            AnalyticsGroupBy.Product => (sample.Line.ProductName ?? sample.Line.Name, null, null, sample.Line.ProductId),
            AnalyticsGroupBy.Brand => (sample.Line.Brand, null, null, null),
            _ => (null, null, null, null)
        };
    }

    private static string BucketKey(bool weekly, DateOnly day)
    {
        if (!weekly)
        {
            return day.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        }

        var monday = day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
        return monday.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static DateOnly ParseBucket(string key, bool weekly) => weekly
        ? DateOnly.ParseExact(key, "yyyy-MM-dd", CultureInfo.InvariantCulture)
        : DateOnly.ParseExact(key + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static List<decimal> Prices(IEnumerable<MatchedLine> lines, string? unit) => lines
        .Where(line => !line.IsReceiptDiscount && ProductUnit.Canonicalize(line.Line.Unit) == unit)
        .Select(line => UnitPrice(line.Line))
        .OfType<decimal>()
        .ToList();

    private static decimal? Price(AnalyticsMetric metric, List<decimal> prices)
    {
        if (prices.Count == 0)
        {
            return null;
        }

        return metric switch
        {
            AnalyticsMetric.MinimumUnitPrice => prices.Min(),
            AnalyticsMetric.MaximumUnitPrice => prices.Max(),
            _ => Round(prices.Average())
        };
    }

    /// <summary>
    /// The shelf price of the product line, before any discount: that is what
    /// «сколько стоил» asks about, and it matches the product card's history.
    /// </summary>
    private static decimal? UnitPrice(AnalyticsLineFact line)
    {
        if (line.UnitPrice is > 0)
        {
            return line.UnitPrice;
        }

        if (line.LineTotal is not > 0)
        {
            return null;
        }

        var quantity = line.Quantity ?? 1m;
        return quantity > 0 ? Round(line.LineTotal.Value / quantity) : null;
    }

    private static decimal Quantity(IEnumerable<MatchedLine> lines, string unit)
    {
        var total = 0m;
        foreach (var line in lines)
        {
            var normalized = QuantityUnit(line.Line.Unit);
            if (normalized.Unit == unit)
            {
                total += (line.Line.Quantity ?? 1m) * normalized.Factor;
            }
        }

        return decimal.Round(total, 3);
    }

    /// <summary>
    /// Quantities, unlike prices, may fold magnitudes: 500 g and 1 kg of the same
    /// cheese add up to 1.5 kg, and a misread unit shifts one line, not an average.
    /// </summary>
    private static (string Unit, decimal Factor) QuantityUnit(string? unit) => ProductUnit.Canonicalize(unit) switch
    {
        "g" => ("kg", 0.001m),
        "dag" => ("kg", 0.01m),
        "ml" => ("l", 0.001m),
        var canonical => (canonical, 1m)
    };

    private static bool InPeriod(AnalyticsPurchaseFact purchase, AnalyticsPlan plan, TimeZoneInfo timeZone)
    {
        var day = LocalDay(purchase.PurchasedAt, timeZone);
        return (plan.From is null || day >= plan.From) && (plan.To is null || day <= plan.To);
    }

    public static DateOnly LocalDay(DateTimeOffset moment, TimeZoneInfo timeZone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(moment, timeZone).DateTime);

    private static string CurrencyOf(AnalyticsPurchaseFact purchase) => purchase.Currency ?? FallbackCurrency;

    private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private sealed record Context(AnalyticsPlan Plan, string Currency, string? QuantityUnit, string? PriceUnit);

    private sealed record MatchedLine(AnalyticsPurchaseFact Purchase, AnalyticsLineFact Line, decimal Discount, DateOnly Day)
    {
        public bool IsReceiptDiscount { get; init; }

        /// <summary>What was actually paid for the line: its total plus its (negative) discounts.</summary>
        public decimal Paid => IsReceiptDiscount ? Line.LineTotal ?? 0m : (Line.LineTotal ?? 0m) + Discount;
    }

    private sealed class LineMatcher
    {
        private readonly AnalyticsPlan _plan;
        private readonly HashSet<Guid> _productIds;
        private readonly HashSet<Guid> _categoryIds;
        private readonly List<string> _terms;
        private readonly HashSet<string> _brands;
        private readonly List<Guid> _tagIds;

        public LineMatcher(AnalyticsPlan plan, AnalyticsVocabulary vocabulary)
        {
            _plan = plan;
            _productIds = plan.ProductIds.ToHashSet();
            // A group includes its subcategories.
            _categoryIds = plan.CategoryIds
                .Concat(vocabulary.Categories
                    .Where(category => category.ParentId is { } parent && plan.CategoryIds.Contains(parent))
                    .Select(category => category.Id))
                .ToHashSet();
            _terms = plan.NameTerms.Select(ProductNameNormalizer.Normalize).Where(term => term.Length > 0).ToList();
            _brands = plan.Brands.Select(ProductNameNormalizer.Normalize).ToHashSet(StringComparer.Ordinal);
            _tagIds = plan.TagIds.Distinct().ToList();
        }

        public bool Matches(AnalyticsLineFact line, bool discounted)
        {
            if (_plan.HasProductSelector && !MatchesSelector(line))
            {
                return false;
            }

            if (_brands.Count > 0 && !_brands.Contains(ProductNameNormalizer.Normalize(line.Brand)))
            {
                return false;
            }

            // Every chosen tag is required, as in the purchases and products filters.
            if (_tagIds.Count > 0 && !_tagIds.All(line.TagIds.Contains))
            {
                return false;
            }

            return _plan.Discount switch
            {
                AnalyticsDiscountFilter.DiscountedOnly => discounted,
                AnalyticsDiscountFilter.NotDiscounted => !discounted,
                _ => true
            };
        }

        private bool MatchesSelector(AnalyticsLineFact line)
        {
            if (line.ProductId is { } productId && _productIds.Contains(productId))
            {
                return true;
            }

            if (line.CategoryId is { } categoryId && _categoryIds.Contains(categoryId))
            {
                return true;
            }

            if (_terms.Count == 0)
            {
                return false;
            }

            var name = " " + ProductNameNormalizer.Normalize(line.Name) + " " + (line.ProductSearchText ?? "") + " ";
            return _terms.Any(term => name.Contains(term, StringComparison.Ordinal));
        }
    }
}
