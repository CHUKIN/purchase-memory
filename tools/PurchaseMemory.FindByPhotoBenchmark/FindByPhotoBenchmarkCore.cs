using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using PurchaseMemory.Application.Products.PhotoNames;
using PurchaseMemory.Application.Products.PhotoSearch;

namespace PurchaseMemory.FindByPhotoBenchmark;

// Network-free core of the «Найти по фото» benchmark: arguments, inputs,
// the budgeted run and the scoring. The test project links this file and
// drives it with a fake matcher; only Program.cs knows about Claude.

/// <param name="Limit">At most this many labelled photos (int.MaxValue: all).</param>
/// <param name="MaxCandidates">The list sent with every photo is cut to this many products, as the app does.</param>
public sealed record BenchmarkArguments(
    string PhotosFolder,
    string LabelsPath,
    string CandidatesPath,
    string OutputPath,
    decimal BudgetUsd,
    string? Model,
    int Limit,
    int MaxCandidates)
{
    public const string Usage =
        "Usage: <photos-folder> <labels.csv> <candidates.json|folder> <out.csv> --budget-usd <amount> [--model <id>] [--limit <n>] [--max-candidates <n>]";

    /// <exception cref="ArgumentException">Anything missing or invalid; nothing has been called yet.</exception>
    public static BenchmarkArguments Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var positional = new List<string>();
        var flags = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Count; index++)
        {
            if (args[index].StartsWith("--", StringComparison.Ordinal))
            {
                if (args[index] is not ("--budget-usd" or "--model" or "--limit" or "--max-candidates"))
                {
                    throw new ArgumentException($"Unknown option {args[index]}. {Usage}");
                }

                if (index + 1 >= args.Count || args[index + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    throw new ArgumentException($"{args[index]} needs a value. {Usage}");
                }

                flags[args[index]] = args[++index];
            }
            else
            {
                positional.Add(args[index]);
            }
        }

        if (positional.Count != 4)
        {
            throw new ArgumentException(Usage);
        }

        // The run spends money: no default budget, ever.
        if (!flags.TryGetValue("--budget-usd", out var budgetText)
            || !decimal.TryParse(budgetText, NumberStyles.Number, CultureInfo.InvariantCulture, out var budget)
            || budget <= 0)
        {
            throw new ArgumentException("--budget-usd is required and must be a positive amount, e.g. --budget-usd 0.10");
        }

        var limit = int.MaxValue;
        if (flags.TryGetValue("--limit", out var limitText)
            && (!int.TryParse(limitText, NumberStyles.None, CultureInfo.InvariantCulture, out limit) || limit < 1))
        {
            throw new ArgumentException("--limit must be a positive whole number.");
        }

        var maxCandidates = 300;
        if (flags.TryGetValue("--max-candidates", out var capText)
            && (!int.TryParse(capText, NumberStyles.None, CultureInfo.InvariantCulture, out maxCandidates) || maxCandidates is < 10 or > 1000))
        {
            throw new ArgumentException("--max-candidates must be between 10 and 1000.");
        }

        return new BenchmarkArguments(
            positional[0], positional[1], positional[2], positional[3], budget,
            flags.TryGetValue("--model", out var model) && model.Trim().Length > 0 ? model.Trim() : null,
            limit, maxCandidates);
    }
}

/// <param name="Expected">Product ids or names (any of them counts as right), or the single word <c>none</c>: the product is not in the list.</param>
public sealed record PhotoLabel(string File, IReadOnlyList<string> Expected)
{
    public bool ExpectsNone => Expected is [var only] && string.Equals(only, "none", StringComparison.OrdinalIgnoreCase);
}

public static class BenchmarkInputs
{
    /// <summary>
    /// UTF-8 CSV with a header row <c>file,expected</c>. <c>expected</c> is a
    /// product id or name; several right answers are separated by <c>;</c>.
    /// </summary>
    public static IReadOnlyList<PhotoLabel> ParseLabels(string csv)
    {
        var labels = new List<PhotoLabel>();
        foreach (var fields in Csv.Parse(csv).Skip(1))
        {
            if (fields.Count < 2 || fields[0].Trim().Length == 0)
            {
                continue;
            }

            var expected = fields[1].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (expected.Length > 0)
            {
                labels.Add(new PhotoLabel(fields[0].Trim(), expected));
            }
        }

        return labels;
    }

    /// <summary>
    /// Either a JSON array of candidates
    /// (<c>productId, receiptName, namePl, nameRu, brand, category</c>) or a
    /// saved <c>GET /api/products</c> page (<c>{"items":[{"id","name",...}]}</c>);
    /// several pages may be passed one after another. Repeated ids are kept once.
    /// </summary>
    public static IReadOnlyList<ProductPhotoCandidate> ParseCandidates(IEnumerable<string> jsonDocuments)
    {
        ArgumentNullException.ThrowIfNull(jsonDocuments);
        var candidates = new List<ProductPhotoCandidate>();
        var seen = new HashSet<Guid>();
        foreach (var json in jsonDocuments)
        {
            using var document = JsonDocument.Parse(json);
            var items = document.RootElement.ValueKind switch
            {
                JsonValueKind.Array => document.RootElement,
                JsonValueKind.Object when document.RootElement.TryGetProperty("items", out var page) && page.ValueKind == JsonValueKind.Array => page,
                _ => throw new JsonException("A candidates file is a JSON array or a catalog page with \"items\".")
            };
            foreach (var item in items.EnumerateArray())
            {
                var id = Text(item, "productId") ?? Text(item, "id");
                var receipt = Text(item, "receiptName") ?? Text(item, "name");
                if (!Guid.TryParse(id, out var productId) || receipt is null)
                {
                    throw new JsonException("Every candidate needs an id (productId or id) and a receipt name (receiptName or name).");
                }

                if (seen.Add(productId))
                {
                    candidates.Add(new ProductPhotoCandidate(
                        productId, receipt, Text(item, "namePl"), Text(item, "nameRu"), Text(item, "brand"),
                        Text(item, "category") ?? Text(item, "categoryName")));
                }
            }
        }

        return candidates;
    }

    /// <summary>
    /// The ids a label accepts; an empty set for <c>none</c>; <c>null</c> when
    /// a name or id matches no candidate (the photo is skipped, nothing spent).
    /// </summary>
    public static IReadOnlySet<Guid>? Resolve(PhotoLabel label, IReadOnlyList<ProductPhotoCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(label);
        if (label.ExpectsNone)
        {
            return new HashSet<Guid>();
        }

        var ids = new HashSet<Guid>();
        foreach (var expected in label.Expected)
        {
            var found = Guid.TryParse(expected, out var id)
                ? candidates.Where(candidate => candidate.ProductId == id).ToList()
                : candidates.Where(candidate => new[] { candidate.NamePl, candidate.NameRu, candidate.ReceiptName }
                    .Any(name => name is not null && string.Equals(name.Trim(), expected, StringComparison.OrdinalIgnoreCase))).ToList();
            if (found.Count == 0)
            {
                return null;
            }

            ids.UnionWith(found.Select(candidate => candidate.ProductId));
        }

        return ids;
    }

    private static string? Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString()!.Trim().Length > 0
            ? value.GetString()!.Trim()
            : null;
}

/// <summary>One labelled photo ready to send (null image: the file could not be decoded).</summary>
public sealed record BenchmarkPhoto(PhotoLabel Label, IReadOnlySet<Guid> ExpectedIds, Func<ProductPhotoImage?> LoadImage);

/// <param name="Matched">Product ids the matcher returned, best first.</param>
public sealed record PhotoOutcome(
    string File,
    bool ExpectsNone,
    IReadOnlyList<Guid> Matched,
    IReadOnlyList<ProductPhotoMatchConfidence> Confidences,
    bool Top1,
    bool Top3,
    long DurationMs,
    long? InputTokens,
    long? OutputTokens,
    decimal ChargedUsd,
    string? Error);

/// <param name="Top1Rate">Share of in-list photos whose first match is right.</param>
/// <param name="Top3Rate">Share of in-list photos with a right match among the (up to) three.</param>
/// <param name="NoneCorrectRate">Share of «not in the list» photos that got no match; null without such photos.</param>
public sealed record BenchmarkSummary(
    int Photos,
    int InListPhotos,
    int NonePhotos,
    int Failed,
    decimal? Top1Rate,
    decimal? Top3Rate,
    decimal? NoneCorrectRate,
    double MeanDurationMs,
    decimal MeanCostUsd,
    decimal TotalCostUsd,
    bool StoppedByBudget);

public sealed record BenchmarkReport(IReadOnlyList<PhotoOutcome> Outcomes, BenchmarkSummary Summary);

/// <summary>
/// Sends the photos one by one and stops before a call could cross the
/// budget. Every call is assumed to cost the larger of
/// <paramref name="worstCaseCallUsd"/> (all output tokens used and a
/// generous input) and the most expensive call so far, so even the last call
/// cannot overshoot. A failed call or one without usage is charged at that
/// assumption.
/// </summary>
/// <param name="worstCaseCallUsd">The most one call can cost; see <see cref="WorstCaseCallUsd"/>.</param>
public sealed class FindByPhotoBenchmarkRunner(
    IProductPhotoMatcher matcher,
    decimal budgetUsd,
    decimal worstCaseCallUsd,
    Action<string>? progress = null)
{
    /// <summary>
    /// Input tokens assumed for one call: the image (~1 050 at 1024 px), the
    /// prompt (~450) and up to 300 candidates (~40 each), rounded well up.
    /// </summary>
    public const int ConservativeInputTokens = 20_000;

    /// <summary>The worst case of one call from list prices per million tokens.</summary>
    public static decimal WorstCaseCallUsd(decimal inputPerMillion, decimal outputPerMillion, int maximumOutputTokens) =>
        decimal.Round((ConservativeInputTokens * inputPerMillion + maximumOutputTokens * outputPerMillion) / 1_000_000m, 6);

    /// <summary>
    /// USD per million tokens used when a call reports usage but no cost: above
    /// any current Claude list price, so the budget errs on the safe side.
    /// </summary>
    public const decimal CeilingInputPerMillion = 30m;

    public const decimal CeilingOutputPerMillion = 150m;

    /// <summary>
    /// The reported cost; without one, the tokens at the ceiling price; without
    /// usage either, the current assumption (whichever of the two is larger).
    /// </summary>
    public static decimal Charge(ProductPhotoMatchReading reading, decimal assumed)
    {
        ArgumentNullException.ThrowIfNull(reading);
        if (reading.CostUsd is { } cost)
        {
            return cost;
        }

        if (reading.InputTokens is null && reading.OutputTokens is null)
        {
            return assumed;
        }

        var ceiling = ((reading.InputTokens ?? 0) * CeilingInputPerMillion + (reading.OutputTokens ?? 0) * CeilingOutputPerMillion) / 1_000_000m;
        return Math.Max(assumed, decimal.Round(ceiling, 6));
    }

    public async Task<BenchmarkReport> RunAsync(
        IReadOnlyList<BenchmarkPhoto> photos,
        IReadOnlyList<ProductPhotoCandidate> candidates,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(photos);
        ArgumentNullException.ThrowIfNull(candidates);
        if (budgetUsd <= 0)
        {
            throw new InvalidOperationException("A positive budget is required.");
        }

        if (worstCaseCallUsd <= 0)
        {
            throw new InvalidOperationException("A positive worst-case call cost is required.");
        }

        var assumed = worstCaseCallUsd;
        var maxObserved = 0m;
        var spent = 0m;
        var stopped = false;
        var outcomes = new List<PhotoOutcome>();
        foreach (var photo in photos)
        {
            if (spent + assumed > budgetUsd)
            {
                stopped = true;
                progress?.Invoke($"Budget: {Usd(spent)} spent, the next call could cross {Usd(budgetUsd)} USD; stopping.");
                break;
            }

            var image = photo.LoadImage();
            if (image is null)
            {
                outcomes.Add(new PhotoOutcome(photo.Label.File, photo.Label.ExpectsNone, [], [], false, false, 0, null, null, 0m, "could not decode"));
                progress?.Invoke($"- {photo.Label.File}: could not decode, skipped (no call)");
                continue;
            }

            var stopwatch = Stopwatch.StartNew();
            try
            {
                var reading = await matcher.MatchAsync(image, candidates, cancellationToken);
                stopwatch.Stop();
                var charged = Charge(reading, assumed);
                spent += charged;
                maxObserved = Math.Max(maxObserved, charged);
                assumed = Math.Max(worstCaseCallUsd, maxObserved);
                var matched = reading.Matches.Select(match => match.ProductId).ToList();
                var (top1, top3) = Score(photo, matched);
                outcomes.Add(new PhotoOutcome(photo.Label.File, photo.Label.ExpectsNone, matched,
                    reading.Matches.Select(match => match.Confidence).ToList(), top1, top3,
                    stopwatch.ElapsedMilliseconds, reading.InputTokens, reading.OutputTokens, charged, null));
                progress?.Invoke($"- {photo.Label.File}: {(top1 ? "top-1" : top3 ? "top-3" : "miss")} ({stopwatch.ElapsedMilliseconds} ms, ${charged.ToString("0.000000", CultureInfo.InvariantCulture)})");
            }
            catch (ProductPhotoSearchUnavailableException exception)
            {
                stopwatch.Stop();
                // No usage reported, but the call may still be billed.
                spent += assumed;
                outcomes.Add(new PhotoOutcome(photo.Label.File, photo.Label.ExpectsNone, [], [], false, false,
                    stopwatch.ElapsedMilliseconds, null, null, assumed, exception.Message));
                progress?.Invoke($"- {photo.Label.File}: FAILED ({exception.Message})");
            }
        }

        return new BenchmarkReport(outcomes, Summarize(outcomes, stopped));
    }

    /// <summary>For «none» a hit is an empty answer; otherwise the first / any of three is right.</summary>
    private static (bool Top1, bool Top3) Score(BenchmarkPhoto photo, List<Guid> matched)
    {
        if (photo.Label.ExpectsNone)
        {
            return (matched.Count == 0, matched.Count == 0);
        }

        return (matched.Count > 0 && photo.ExpectedIds.Contains(matched[0]),
            matched.Take(ProductPhotoSearchRules.MaximumMatches).Any(photo.ExpectedIds.Contains));
    }

    public static BenchmarkSummary Summarize(IReadOnlyList<PhotoOutcome> outcomes, bool stoppedByBudget)
    {
        ArgumentNullException.ThrowIfNull(outcomes);
        var answered = outcomes.Where(outcome => outcome.Error is null).ToList();
        var inList = answered.Where(outcome => !outcome.ExpectsNone).ToList();
        var none = answered.Where(outcome => outcome.ExpectsNone).ToList();
        var called = outcomes.Where(outcome => outcome.Error != "could not decode").ToList();
        var total = called.Sum(outcome => outcome.ChargedUsd);
        return new BenchmarkSummary(
            outcomes.Count,
            inList.Count,
            none.Count,
            outcomes.Count - answered.Count,
            Rate(inList.Count(outcome => outcome.Top1), inList.Count),
            Rate(inList.Count(outcome => outcome.Top3), inList.Count),
            Rate(none.Count(outcome => outcome.Top1), none.Count),
            answered.Count == 0 ? 0 : answered.Average(outcome => (double)outcome.DurationMs),
            called.Count == 0 ? 0m : decimal.Round(total / called.Count, 6),
            total,
            stoppedByBudget);
    }

    private static decimal? Rate(int hits, int count) => count == 0 ? null : decimal.Round((decimal)hits / count, 3);

    private static string Usd(decimal value) => value.ToString("0.000000", CultureInfo.InvariantCulture);
}

public static class BenchmarkOutput
{
    public static IReadOnlyList<string> CsvRows(BenchmarkReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var rows = new List<string> { "file,expects_none,top1,top3,matched_ids,confidences,ms,input_tokens,output_tokens,cost_usd,error" };
        rows.AddRange(report.Outcomes.Select(outcome => string.Join(',',
            Csv.Quote(outcome.File),
            outcome.ExpectsNone ? "1" : "0",
            outcome.Top1 ? "1" : "0",
            outcome.Top3 ? "1" : "0",
            Csv.Quote(string.Join(';', outcome.Matched)),
            Csv.Quote(string.Join(';', outcome.Confidences.Select(confidence => confidence.ToString().ToLowerInvariant()))),
            outcome.DurationMs.ToString(CultureInfo.InvariantCulture),
            outcome.InputTokens?.ToString(CultureInfo.InvariantCulture) ?? "",
            outcome.OutputTokens?.ToString(CultureInfo.InvariantCulture) ?? "",
            outcome.ChargedUsd.ToString("0.000000", CultureInfo.InvariantCulture),
            Csv.Quote(outcome.Error))));
        return rows;
    }

    public static string SummaryText(BenchmarkSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        static string Percent(decimal? rate) => rate is null ? "n/a" : (rate.Value * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%";
        return string.Join(Environment.NewLine,
            $"photos {summary.Photos} (in list {summary.InListPhotos}, not in list {summary.NonePhotos}, failed {summary.Failed})",
            $"top-1 {Percent(summary.Top1Rate)}; top-3 {Percent(summary.Top3Rate)}; «not in list» answered empty {Percent(summary.NoneCorrectRate)}",
            $"mean time {summary.MeanDurationMs.ToString("0", CultureInfo.InvariantCulture)} ms; mean cost {summary.MeanCostUsd.ToString("0.000000", CultureInfo.InvariantCulture)} USD per search; total {summary.TotalCostUsd.ToString("0.000000", CultureInfo.InvariantCulture)} USD"
                + (summary.StoppedByBudget ? "; STOPPED by the budget" : ""));
    }
}

/// <summary>Minimal CSV: quoted fields, "" as an escaped quote, commas and line breaks inside quotes.</summary>
internal static class Csv
{
    public static string Quote(string? value) =>
        value is null ? "" : "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    public static List<List<string>> Parse(string text)
    {
        var records = new List<List<string>>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else field.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { fields.Add(field.ToString()); field.Clear(); }
            else if (c == '\n' || c == '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                fields.Add(field.ToString());
                field.Clear();
                if (fields.Count > 1 || fields[0].Length > 0) records.Add(fields);
                fields = [];
            }
            else if (c != '﻿') field.Append(c);
        }

        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            records.Add(fields);
        }

        return records;
    }
}
