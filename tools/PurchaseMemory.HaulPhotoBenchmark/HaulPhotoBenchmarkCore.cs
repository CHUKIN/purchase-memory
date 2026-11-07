using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using PurchaseMemory.Application.Purchases.HaulPhoto;

namespace PurchaseMemory.HaulPhotoBenchmark;

// Network-free core of the «Сфотографировать покупки» benchmark: arguments,
// inputs, the budgeted run and the scoring. The test project links this file
// and drives it with a fake matcher; only Program.cs knows about Claude.

/// <param name="Limit">At most this many labelled photos (int.MaxValue: all).</param>
/// <param name="CropsFolder">When set, every proposed item is cut out and written here for a visual check of the boxes.</param>
public sealed record BenchmarkArguments(
    string PhotosFolder,
    string LabelsPath,
    string OutputPath,
    decimal BudgetUsd,
    string? Model,
    string? Effort,
    int Limit,
    string? CropsFolder)
{
    public const string Usage =
        "Usage: <photos-folder> <hauls.json> <out.csv> --budget-usd <amount> [--model <id>] [--effort low|medium|high] [--limit <n>] [--crops <folder>]";

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
                if (args[index] is not ("--budget-usd" or "--model" or "--effort" or "--limit" or "--crops"))
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

        if (positional.Count != 3)
        {
            throw new ArgumentException(Usage);
        }

        // The run spends money: no default budget, ever.
        if (!flags.TryGetValue("--budget-usd", out var budgetText)
            || !decimal.TryParse(budgetText, NumberStyles.Number, CultureInfo.InvariantCulture, out var budget)
            || budget <= 0)
        {
            throw new ArgumentException("--budget-usd is required and must be a positive amount, e.g. --budget-usd 0.50");
        }

        var limit = int.MaxValue;
        if (flags.TryGetValue("--limit", out var limitText)
            && (!int.TryParse(limitText, NumberStyles.None, CultureInfo.InvariantCulture, out limit) || limit < 1))
        {
            throw new ArgumentException("--limit must be a positive whole number.");
        }

        string? effort = null;
        if (flags.TryGetValue("--effort", out var effortText))
        {
            effort = effortText.Trim().ToLowerInvariant();
            if (effort is not ("low" or "medium" or "high"))
            {
                throw new ArgumentException("--effort must be low, medium or high.");
            }
        }

        return new BenchmarkArguments(
            positional[0], positional[1], positional[2], budget,
            flags.TryGetValue("--model", out var model) && model.Trim().Length > 0 ? model.Trim() : null,
            effort,
            limit,
            flags.TryGetValue("--crops", out var crops) && crops.Trim().Length > 0 ? crops.Trim() : null);
    }
}

/// <summary>One labelled haul photo: the receipt's product lines and the lines whose items are on the photo.</summary>
/// <param name="Expected">Line names, one entry per visible item (a line twice when two of its items are on the photo).</param>
public sealed record HaulLabel(string File, IReadOnlyList<HaulPhotoLine> Lines, IReadOnlyList<string> Expected);

public static class BenchmarkInputs
{
    /// <summary>
    /// A JSON array, one object per photo:
    /// <c>{"file": "IMG_1.jpg", "lines": [...] | "purchase": "purchase-1.json", "expected": ["MLEKO UHT 3,2% 1L", ...]}</c>.
    /// <c>lines</c> entries are <c>{"name", "product"?, "quantity"?, "unit"?}</c>;
    /// <c>purchase</c> names a saved <c>GET /api/purchases/{id}</c> answer next to
    /// the labels file (its discount lines are left out). <c>expected</c> lists
    /// the receipt name (or product name) of every item visible on the photo.
    /// </summary>
    /// <param name="readSibling">Reads a file named in <c>purchase</c>.</param>
    public static IReadOnlyList<HaulLabel> ParseLabels(string json, Func<string, string> readSibling)
    {
        ArgumentNullException.ThrowIfNull(readSibling);
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("The labels file is a JSON array of photos.");
        }

        var labels = new List<HaulLabel>();
        foreach (var entry in document.RootElement.EnumerateArray())
        {
            var file = Text(entry, "file") ?? throw new JsonException("Every photo needs a \"file\".");
            IReadOnlyList<HaulPhotoLine> lines;
            if (entry.TryGetProperty("lines", out var inline) && inline.ValueKind == JsonValueKind.Array)
            {
                lines = ReadLines(inline, "name", "product");
            }
            else if (Text(entry, "purchase") is { } purchaseFile)
            {
                using var purchase = JsonDocument.Parse(readSibling(purchaseFile));
                if (!purchase.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                {
                    throw new JsonException($"{purchaseFile} is not a saved purchase (no \"items\").");
                }

                lines = ReadLines(items, "name", "productNamePl");
            }
            else
            {
                throw new JsonException($"{file}: give either \"lines\" or \"purchase\".");
            }

            var expected = entry.TryGetProperty("expected", out var expectedElement) && expectedElement.ValueKind == JsonValueKind.Array
                ? expectedElement.EnumerateArray()
                    .Where(value => value.ValueKind == JsonValueKind.String && value.GetString()!.Trim().Length > 0)
                    .Select(value => value.GetString()!.Trim())
                    .ToList()
                : throw new JsonException($"{file}: \"expected\" must be a list of line names.");
            labels.Add(new HaulLabel(file, lines, expected));
        }

        return labels;
    }

    private static List<HaulPhotoLine> ReadLines(JsonElement items, string nameField, string productField)
    {
        var lines = new List<HaulPhotoLine>();
        foreach (var item in items.EnumerateArray())
        {
            if (string.Equals(Text(item, "kind"), "discount", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var name = Text(item, nameField) ?? throw new JsonException("Every line needs a name.");
            decimal? quantity = item.TryGetProperty("quantity", out var number) && number.ValueKind == JsonValueKind.Number
                ? number.GetDecimal()
                : null;
            // Synthetic ids: the benchmark has no database, only names.
            lines.Add(new HaulPhotoLine(Guid.NewGuid(), name, Text(item, productField), quantity, Text(item, "unit")));
        }

        return lines;
    }

    /// <summary>
    /// The expected items as line ids; null when a name matches no line (the
    /// photo is skipped, nothing spent). A name matches a line's receipt text
    /// or product name, ignoring case; the first matching line wins.
    /// </summary>
    public static IReadOnlyList<Guid>? Resolve(HaulLabel label)
    {
        ArgumentNullException.ThrowIfNull(label);
        var ids = new List<Guid>();
        foreach (var name in label.Expected)
        {
            var line = label.Lines.FirstOrDefault(line =>
                string.Equals(line.ReceiptName.Trim(), name, StringComparison.OrdinalIgnoreCase)
                || (line.ProductName is not null && string.Equals(line.ProductName.Trim(), name, StringComparison.OrdinalIgnoreCase)));
            if (line is null)
            {
                return null;
            }

            ids.Add(line.LineId);
        }

        return ids;
    }

    private static string? Text(JsonElement item, string name) =>
        item.ValueKind == JsonValueKind.Object
        && item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString()!.Trim().Length > 0
            ? value.GetString()!.Trim()
            : null;
}

/// <summary>One labelled photo ready to send (null image: the file could not be decoded).</summary>
/// <param name="ExpectedLineIds">One entry per visible item.</param>
public sealed record BenchmarkPhoto(HaulLabel Label, IReadOnlyList<Guid> ExpectedLineIds, Func<HaulPhotoImage?> LoadImage);

/// <param name="Correct">Proposed pairs whose line is among the expected items (counted as a multiset).</param>
/// <param name="Wrong">Proposed pairs beyond what is expected for their line.</param>
/// <param name="Missed">Expected items no proposed pair covers.</param>
/// <param name="Unpaired">Items found without a proposed line.</param>
public sealed record PhotoOutcome(
    string File,
    int Expected,
    int Found,
    int Correct,
    int Wrong,
    int Missed,
    int Unpaired,
    long DurationMs,
    long? InputTokens,
    long? OutputTokens,
    decimal ChargedUsd,
    string? Error);

/// <param name="Precision">Correct / (correct + wrong) over all answered photos; null without pairs.</param>
/// <param name="Recall">Correct / expected items over all answered photos; null without expected items.</param>
public sealed record BenchmarkSummary(
    int Photos,
    int Failed,
    int ExpectedItems,
    int CorrectPairs,
    int WrongPairs,
    int MissedItems,
    int UnpairedItems,
    decimal? Precision,
    decimal? Recall,
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
/// assumption; usage without a cost at a ceiling price.
/// </summary>
/// <param name="worstCaseCallUsd">The most one call can cost; see <see cref="WorstCaseCallUsd"/>.</param>
public sealed class HaulPhotoBenchmarkRunner(
    IHaulPhotoMatcher matcher,
    decimal budgetUsd,
    decimal worstCaseCallUsd,
    Action<string>? progress = null,
    Action<BenchmarkPhoto, HaulPhotoImage, HaulPhotoReading>? onAnswer = null)
{
    /// <summary>
    /// Input tokens assumed for one call: the image (≤ 1.15 MP ≈ 1 600 tokens),
    /// the prompt (~700) and 40 lines (~1 200), rounded well up.
    /// </summary>
    public const int ConservativeInputTokens = 6_000;

    /// <summary>The worst case of one call from list prices per million tokens.</summary>
    public static decimal WorstCaseCallUsd(decimal inputPerMillion, decimal outputPerMillion, int maximumOutputTokens) =>
        decimal.Round((ConservativeInputTokens * inputPerMillion + maximumOutputTokens * outputPerMillion) / 1_000_000m, 6);

    /// <summary>USD per million tokens when a call reports usage but no cost: above any current Claude list price.</summary>
    public const decimal CeilingInputPerMillion = 30m;

    public const decimal CeilingOutputPerMillion = 150m;

    public static decimal Charge(HaulPhotoReading reading, decimal assumed)
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

    public async Task<BenchmarkReport> RunAsync(IReadOnlyList<BenchmarkPhoto> photos, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(photos);
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
                outcomes.Add(new PhotoOutcome(photo.Label.File, photo.ExpectedLineIds.Count, 0, 0, 0, 0, 0, 0, null, null, 0m, NotDecoded));
                progress?.Invoke($"- {photo.Label.File}: could not decode, skipped (no call)");
                continue;
            }

            var stopwatch = Stopwatch.StartNew();
            try
            {
                var reading = await matcher.MatchAsync(image, photo.Label.Lines, cancellationToken);
                stopwatch.Stop();
                var charged = Charge(reading, assumed);
                spent += charged;
                maxObserved = Math.Max(maxObserved, charged);
                assumed = Math.Max(worstCaseCallUsd, maxObserved);
                var score = Score(photo.ExpectedLineIds, reading.Items);
                outcomes.Add(new PhotoOutcome(photo.Label.File, photo.ExpectedLineIds.Count, reading.Items.Count,
                    score.Correct, score.Wrong, score.Missed, score.Unpaired,
                    stopwatch.ElapsedMilliseconds, reading.InputTokens, reading.OutputTokens, charged, null));
                onAnswer?.Invoke(photo, image, reading);
                progress?.Invoke($"- {photo.Label.File}: {score.Correct} correct, {score.Wrong} wrong, {score.Missed} missed, {score.Unpaired} unpaired ({stopwatch.ElapsedMilliseconds} ms, ${Usd(charged)})");
            }
            catch (HaulPhotoUnavailableException exception)
            {
                stopwatch.Stop();
                // No usage reported, but the call may still be billed.
                spent += assumed;
                outcomes.Add(new PhotoOutcome(photo.Label.File, photo.ExpectedLineIds.Count, 0, 0, 0, 0, 0,
                    stopwatch.ElapsedMilliseconds, null, null, assumed, exception.Message));
                progress?.Invoke($"- {photo.Label.File}: FAILED ({exception.Message})");
            }
        }

        return new BenchmarkReport(outcomes, Summarize(outcomes, stopped));
    }

    public const string NotDecoded = "could not decode";

    /// <summary>
    /// Multiset comparison of proposed lines with expected lines: a line
    /// expected twice and proposed three times is two correct and one wrong.
    /// Boxes are not scored (no box labels); <c>--crops</c> lets a person look.
    /// </summary>
    public static (int Correct, int Wrong, int Missed, int Unpaired) Score(
        IReadOnlyList<Guid> expectedLineIds,
        IReadOnlyList<HaulPhotoDetection> items)
    {
        ArgumentNullException.ThrowIfNull(expectedLineIds);
        ArgumentNullException.ThrowIfNull(items);
        var remaining = expectedLineIds.GroupBy(id => id).ToDictionary(group => group.Key, group => group.Count());
        int correct = 0, wrong = 0, unpaired = 0;
        foreach (var item in items)
        {
            if (item.LineId is not { } lineId)
            {
                unpaired++;
            }
            else if (remaining.TryGetValue(lineId, out var left) && left > 0)
            {
                remaining[lineId] = left - 1;
                correct++;
            }
            else
            {
                wrong++;
            }
        }

        return (correct, wrong, expectedLineIds.Count - correct, unpaired);
    }

    public static BenchmarkSummary Summarize(IReadOnlyList<PhotoOutcome> outcomes, bool stoppedByBudget)
    {
        ArgumentNullException.ThrowIfNull(outcomes);
        var answered = outcomes.Where(outcome => outcome.Error is null).ToList();
        var called = outcomes.Where(outcome => outcome.Error != NotDecoded).ToList();
        var correct = answered.Sum(outcome => outcome.Correct);
        var wrong = answered.Sum(outcome => outcome.Wrong);
        var expected = answered.Sum(outcome => outcome.Expected);
        var total = called.Sum(outcome => outcome.ChargedUsd);
        return new BenchmarkSummary(
            outcomes.Count,
            outcomes.Count - answered.Count,
            expected,
            correct,
            wrong,
            answered.Sum(outcome => outcome.Missed),
            answered.Sum(outcome => outcome.Unpaired),
            correct + wrong == 0 ? null : decimal.Round((decimal)correct / (correct + wrong), 3),
            expected == 0 ? null : decimal.Round((decimal)correct / expected, 3),
            answered.Count == 0 ? 0 : answered.Average(outcome => (double)outcome.DurationMs),
            called.Count == 0 ? 0m : decimal.Round(total / called.Count, 6),
            total,
            stoppedByBudget);
    }

    private static string Usd(decimal value) => value.ToString("0.000000", CultureInfo.InvariantCulture);
}

public static class BenchmarkOutput
{
    public static IReadOnlyList<string> CsvRows(BenchmarkReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var rows = new List<string> { "file,expected,found,correct,wrong,missed,unpaired,ms,input_tokens,output_tokens,cost_usd,error" };
        rows.AddRange(report.Outcomes.Select(outcome => string.Join(',',
            Quote(outcome.File),
            outcome.Expected.ToString(CultureInfo.InvariantCulture),
            outcome.Found.ToString(CultureInfo.InvariantCulture),
            outcome.Correct.ToString(CultureInfo.InvariantCulture),
            outcome.Wrong.ToString(CultureInfo.InvariantCulture),
            outcome.Missed.ToString(CultureInfo.InvariantCulture),
            outcome.Unpaired.ToString(CultureInfo.InvariantCulture),
            outcome.DurationMs.ToString(CultureInfo.InvariantCulture),
            outcome.InputTokens?.ToString(CultureInfo.InvariantCulture) ?? "",
            outcome.OutputTokens?.ToString(CultureInfo.InvariantCulture) ?? "",
            outcome.ChargedUsd.ToString("0.000000", CultureInfo.InvariantCulture),
            Quote(outcome.Error))));
        return rows;
    }

    public static string SummaryText(BenchmarkSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        static string Percent(decimal? rate) => rate is null ? "n/a" : (rate.Value * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%";
        return string.Join(Environment.NewLine,
            $"photos {summary.Photos} (failed {summary.Failed}); expected items {summary.ExpectedItems}",
            $"correct pairs {summary.CorrectPairs}; wrong pairs {summary.WrongPairs}; missed {summary.MissedItems}; found without a line {summary.UnpairedItems}",
            $"precision {Percent(summary.Precision)}; recall {Percent(summary.Recall)}",
            $"mean time {summary.MeanDurationMs.ToString("0", CultureInfo.InvariantCulture)} ms; mean cost {summary.MeanCostUsd.ToString("0.000000", CultureInfo.InvariantCulture)} USD per photo; total {summary.TotalCostUsd.ToString("0.000000", CultureInfo.InvariantCulture)} USD"
                + (summary.StoppedByBudget ? "; STOPPED by the budget" : ""));
    }

    private static string Quote(string? value) =>
        value is null ? "" : "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
