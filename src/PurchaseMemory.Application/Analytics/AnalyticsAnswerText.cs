using System.Globalization;

namespace PurchaseMemory.Application.Analytics;

/// <summary>
/// The one-sentence conclusion, written by templates from computed numbers —
/// never by a model, so it cannot state a figure the database did not return.
/// It deliberately does not repeat what was asked about: the filter chips
/// beside it show the interpretation, and category names are translated there.
/// Tags are the exception: they are the household's own words and can quietly
/// narrow an answer a lot, so a tag filter is named in a closing sentence. That a
/// breakdown by tag counts a product under each of its tags is explained beside
/// the breakdown (and in <see cref="AnalyticsCoverage.MultiTaggedLines"/>), not here.
/// </summary>
public static class AnalyticsAnswerText
{
    /// <param name="tagNames">Names of the plan's tag filter, in plan order; empty or null when there is none.</param>
    public static string Conclusion(AnalyticsResult result, string? locale, IReadOnlyList<string>? tagNames = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        var english = string.Equals(locale, "en", StringComparison.OrdinalIgnoreCase);
        var culture = CultureInfo.GetCultureInfo(english ? "en-GB" : "ru-RU");
        var text = new Templates(english, culture, result.Currency);
        var sentence = Sentence(result, text);

        return tagNames is { Count: > 0 } ? sentence + " " + text.OnlyTagged(tagNames) : sentence;
    }

    private static string Sentence(AnalyticsResult result, Templates text)
    {
        if (!result.HasData)
        {
            return result.Metric == AnalyticsMetric.Exists ? text.NotBought : text.NoData;
        }

        var purchases = Kpi(result, "purchaseCount") ?? 0m;
        var lines = Kpi(result, "lineCount") ?? 0m;
        switch (result.Metric)
        {
            case AnalyticsMetric.Spend:
            {
                var sentence = text.Spent(Kpi(result, "spend") ?? 0m, (int)lines, (int)purchases);
                return Kpi(result, "discounts") is { } discounts ? sentence + " " + text.Saved(-discounts) : sentence;
            }

            case AnalyticsMetric.Quantity:
            {
                var quantity = result.Kpis.FirstOrDefault(kpi => kpi.Code == "quantity");
                return quantity?.Value is { } value
                    ? text.Bought(value, quantity.Unit, (int)lines, (int)purchases)
                    : text.Lines((int)lines, (int)purchases);
            }

            case AnalyticsMetric.PurchaseCount:
                return text.Receipts((int)purchases, (int)lines);

            case AnalyticsMetric.AverageUnitPrice:
            case AnalyticsMetric.MinimumUnitPrice:
            case AnalyticsMetric.MaximumUnitPrice:
            {
                var priceCount = result.Kpis.FirstOrDefault(kpi => kpi.Code == "priceCount");
                var code = result.Metric switch
                {
                    AnalyticsMetric.AverageUnitPrice => "averagePrice",
                    AnalyticsMetric.MinimumUnitPrice => "minimumPrice",
                    _ => "maximumPrice"
                };
                return Kpi(result, code) is { } price
                    ? text.Price(result.Metric, price, priceCount?.Unit, (int)(priceCount?.Value ?? 0m),
                        Kpi(result, "minimumPrice"), Kpi(result, "maximumPrice"))
                    : text.NoPrices;
            }

            case AnalyticsMetric.Exists:
            {
                var last = result.Kpis.FirstOrDefault(kpi => kpi.Code == "lastPurchase")?.Date;
                return text.YesBought((int)purchases, last);
            }

            case AnalyticsMetric.Top:
            {
                var leader = result.Groups.Count > 0 ? result.Groups[0] : null;
                return leader is null
                    ? text.Spent(Kpi(result, "spend") ?? 0m, (int)lines, (int)purchases)
                    : text.Leader(leader.Label, leader.Value, Kpi(result, "spend") ?? 0m, result.GroupBy);
            }

            default:
                return text.NoData;
        }
    }

    private static decimal? Kpi(AnalyticsResult result, string code) =>
        result.Kpis.FirstOrDefault(kpi => kpi.Code == code)?.Value;

    private sealed class Templates(bool english, CultureInfo culture, string currency)
    {
        public string NoData => english
            ? "No purchases match these filters."
            : "По этим условиям покупок не нашлось.";

        public string NotBought => english
            ? "No, no such purchases were found."
            : "Нет, таких покупок не нашлось.";

        public string NoPrices => english
            ? "The matching lines have no prices to compare."
            : "У подходящих позиций нет цен для сравнения.";

        public string Spent(decimal amount, int lines, int purchases) => english
            ? $"Spent {Money(amount)}: {Count(lines, "line", "lines")} on {Count(purchases, "receipt", "receipts")}."
            : $"Потрачено {Money(amount)}: {lines} {Ru(lines, "позиция", "позиции", "позиций")} в {purchases} {Ru(purchases, "чеке", "чеках", "чеках")}.";

        public string Saved(decimal amount) => english
            ? $"Discounts saved {Money(amount)}."
            : $"Скидки сэкономили {Money(amount)}.";

        public string Bought(decimal quantity, string? unit, int lines, int purchases) => english
            ? $"Bought {Number(quantity)} {Unit(unit)} in {Count(lines, "line", "lines")} on {Count(purchases, "receipt", "receipts")}."
            : $"Куплено {Number(quantity)} {Unit(unit)}: {lines} {Ru(lines, "позиция", "позиции", "позиций")} в {purchases} {Ru(purchases, "чеке", "чеках", "чеках")}.";

        public string Lines(int lines, int purchases) => english
            ? $"{Count(lines, "line", "lines")} on {Count(purchases, "receipt", "receipts")}."
            : $"{lines} {Ru(lines, "позиция", "позиции", "позиций")} в {purchases} {Ru(purchases, "чеке", "чеках", "чеках")}.";

        public string Receipts(int purchases, int lines) => english
            ? $"Bought on {Count(purchases, "receipt", "receipts")} ({Count(lines, "line", "lines")})."
            : $"Покупали по {purchases} {Ru(purchases, "чеку", "чекам", "чекам")} ({lines} {Ru(lines, "позиция", "позиции", "позиций")}).";

        public string Price(AnalyticsMetric metric, decimal price, string? unit, int count, decimal? minimum, decimal? maximum)
        {
            // «per pcs» reads wrong; a price is always per one piece.
            var per = english && unit is null or "szt" ? "piece" : Unit(unit);
            return (metric, english) switch
            {
                (AnalyticsMetric.AverageUnitPrice, true) =>
                    $"Average price {Money(price)} per {per} over {Count(count, "purchase", "purchases")}" +
                    (minimum is { } low && maximum is { } high && low != high ? $", from {Money(low)} to {Money(high)}." : "."),
                (AnalyticsMetric.AverageUnitPrice, false) =>
                    $"Средняя цена — {Money(price)} за {per} по {count} {Ru(count, "покупке", "покупкам", "покупкам")}" +
                    (minimum is { } low && maximum is { } high && low != high ? $", от {Money(low)} до {Money(high)}." : "."),
                (AnalyticsMetric.MinimumUnitPrice, true) => $"Lowest price {Money(price)} per {per}.",
                (AnalyticsMetric.MinimumUnitPrice, false) => $"Самая низкая цена — {Money(price)} за {per}.",
                (_, true) => $"Highest price {Money(price)} per {per}.",
                _ => $"Самая высокая цена — {Money(price)} за {per}."
            };
        }

        public string YesBought(int purchases, DateOnly? last)
        {
            var date = last?.ToString(english ? "d MMM yyyy" : "d MMMM yyyy", culture);
            return english
                ? $"Yes: {Count(purchases, "receipt", "receipts")}" + (date is null ? "." : $", last on {date}.")
                : $"Да, покупали: {purchases} {Ru(purchases, "чек", "чека", "чеков")}" + (date is null ? "." : $", последний раз — {date}.");
        }

        public string OnlyTagged(IReadOnlyList<string> tags)
        {
            var names = string.Join(", ", tags.Select(tag => $"«{tag}»"));
            return (english, tags.Count) switch
            {
                (true, 1) => $"Only products tagged {names} are counted.",
                (true, _) => $"Only products with all the tags {names} are counted.",
                (false, 1) => $"Учтены только товары с тегом {names}.",
                _ => $"Учтены только товары со всеми тегами {names}."
            };
        }

        public string Leader(string? label, decimal value, decimal total, AnalyticsGroupBy groupBy)
        {
            var name = label ?? (groupBy, english) switch
            {
                (AnalyticsGroupBy.Tag, true) => "no tag",
                (AnalyticsGroupBy.Tag, false) => "без тега",
                (_, true) => "unnamed",
                _ => "без названия"
            };
            return english
                ? $"Most was spent on «{name}»: {Money(value)} of {Money(total)}."
                : $"Больше всего потрачено на «{name}»: {Money(value)} из {Money(total)}.";
        }

        private string Money(decimal amount)
        {
            var symbol = currency switch
            {
                "PLN" => "zł",
                "EUR" => "€",
                _ => currency
            };
            return amount.ToString("N2", culture) + "\u00a0" + symbol;
        }

        private string Number(decimal value) => value.ToString(value == decimal.Truncate(value) ? "N0" : "0.###", culture);

        private string Unit(string? unit) => (unit, english) switch
        {
            ("kg", false) => "кг",
            ("l", false) => "л",
            ("szt", false) => "шт.",
            ("opak", false) => "упак.",
            ("m", false) => "м",
            ("szt", true) => "pcs",
            ("opak", true) => "packs",
            (null, true) => "pcs",
            (null, false) => "шт.",
            _ => unit!
        };

        private static string Count(int count, string one, string many) =>
            count.ToString(CultureInfo.InvariantCulture) + " " + (count == 1 ? one : many);

        /// <summary>Russian plural: 1 чек, 2 чека, 5 чеков, 21 чек.</summary>
        private static string Ru(int count, string one, string few, string many)
        {
            var mod100 = Math.Abs(count) % 100;
            var mod10 = mod100 % 10;
            if (mod100 is >= 11 and <= 14) return many;
            return mod10 switch
            {
                1 => one,
                >= 2 and <= 4 => few,
                _ => many
            };
        }
    }
}
