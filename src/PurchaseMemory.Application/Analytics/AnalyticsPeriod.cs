using System.Globalization;
using System.Text.Json.Serialization;

namespace PurchaseMemory.Application.Analytics;

/// <summary>
/// How the planner names a period. The model never does date arithmetic: it
/// says «last month» or «August», and <see cref="AnalyticsPeriodResolver"/>
/// turns that into calendar days against the server's «today».
/// </summary>
[JsonConverter(typeof(CamelCaseEnumConverter<AnalyticsPeriodPreset>))]
public enum AnalyticsPeriodPreset
{
    /// <summary>No period at all: the whole history.</summary>
    None,
    ThisYear,
    LastYear,
    ThisMonth,
    LastMonth,
    Last7Days,
    Last30Days,
    Last12Months,

    /// <summary><see cref="AnalyticsPeriodRequest.Month"/>, in <see cref="AnalyticsPeriodRequest.Year"/> or the latest one not in the future.</summary>
    Month,

    /// <summary>The whole <see cref="AnalyticsPeriodRequest.Year"/>.</summary>
    Year,

    /// <summary>Explicit ISO dates in <see cref="AnalyticsPeriodRequest.From"/> / <see cref="AnalyticsPeriodRequest.To"/>.</summary>
    Custom,

    /// <summary>Keep the period of the previous plan (follow-up questions).</summary>
    Keep
}

public sealed record AnalyticsPeriodRequest(
    AnalyticsPeriodPreset Preset,
    string? From = null,
    string? To = null,
    int? Month = null,
    int? Year = null);

public sealed record ResolvedPeriod(DateOnly? From, DateOnly? To, bool Valid);

public static class AnalyticsPeriodResolver
{
    /// <summary>
    /// Calendar days for a named period. An unusable request (a month of 13,
    /// unparsable dates) comes back with <see cref="ResolvedPeriod.Valid"/> false
    /// and no bounds, and the validator reports it instead of guessing.
    /// </summary>
    public static ResolvedPeriod Resolve(AnalyticsPeriodRequest request, DateOnly today, ResolvedPeriod? previous = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        var firstOfMonth = new DateOnly(today.Year, today.Month, 1);

        return request.Preset switch
        {
            AnalyticsPeriodPreset.None => new ResolvedPeriod(null, null, true),
            AnalyticsPeriodPreset.Keep => previous ?? new ResolvedPeriod(null, null, true),
            AnalyticsPeriodPreset.ThisYear => new ResolvedPeriod(new DateOnly(today.Year, 1, 1), today, true),
            AnalyticsPeriodPreset.LastYear => new ResolvedPeriod(
                new DateOnly(today.Year - 1, 1, 1), new DateOnly(today.Year - 1, 12, 31), true),
            AnalyticsPeriodPreset.ThisMonth => new ResolvedPeriod(firstOfMonth, today, true),
            AnalyticsPeriodPreset.LastMonth => new ResolvedPeriod(
                firstOfMonth.AddMonths(-1), firstOfMonth.AddDays(-1), true),
            AnalyticsPeriodPreset.Last7Days => new ResolvedPeriod(today.AddDays(-6), today, true),
            AnalyticsPeriodPreset.Last30Days => new ResolvedPeriod(today.AddDays(-29), today, true),
            AnalyticsPeriodPreset.Last12Months => new ResolvedPeriod(today.AddMonths(-12).AddDays(1), today, true),
            AnalyticsPeriodPreset.Month => ResolveMonth(request, today),
            AnalyticsPeriodPreset.Year => request.Year is { } year && year is >= 1 and <= 9999
                ? new ResolvedPeriod(new DateOnly(year, 1, 1), new DateOnly(year, 12, 31), true)
                : new ResolvedPeriod(null, null, false),
            AnalyticsPeriodPreset.Custom => ResolveCustom(request),
            _ => new ResolvedPeriod(null, null, false)
        };
    }

    private static ResolvedPeriod ResolveMonth(AnalyticsPeriodRequest request, DateOnly today)
    {
        if (request.Month is not { } month || month is < 1 or > 12)
        {
            return new ResolvedPeriod(null, null, false);
        }

        // «в августе» without a year is the latest August that has started.
        var year = request.Year ?? (month <= today.Month ? today.Year : today.Year - 1);
        if (year is < 1 or > 9999)
        {
            return new ResolvedPeriod(null, null, false);
        }

        var first = new DateOnly(year, month, 1);
        return new ResolvedPeriod(first, first.AddMonths(1).AddDays(-1), true);
    }

    private static ResolvedPeriod ResolveCustom(AnalyticsPeriodRequest request)
    {
        var from = ParseDay(request.From, out var fromValid);
        var to = ParseDay(request.To, out var toValid);
        return fromValid && toValid && (from is not null || to is not null)
            ? new ResolvedPeriod(from, to, true)
            : new ResolvedPeriod(null, null, false);
    }

    private static DateOnly? ParseDay(string? text, out bool valid)
    {
        valid = true;
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            return day;
        }

        valid = false;
        return null;
    }
}
