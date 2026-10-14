using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PurchaseMemory.Application.Receipts;
using PurchaseMemory.Infrastructure.Recognition;

/// <summary>
/// <c>anthropic</c> sub-command: photo → Claude through the production
/// AnthropicStructuredVisionProvider and ReceiptAnalysisWorkflow (prompt, adapted
/// schema, parser, repair retry). Only that provider is constructed, so no other
/// paid service can be called. Writes one JSON per receipt in the format
/// score_items.py reads, with usage, cost and bounding-box fill counts. The
/// <c>parsed</c> block also carries each line's <c>box</c> ([x, y, width,
/// height] fractions) and <c>totalBox</c>, the shape ocr_compare's report reads,
/// so a run written to <c>results/ocr-compare/runs/&lt;label&gt;/</c> is scored for
/// box hits by <c>run_ocr_compare.py report</c> as well. <c>--contract</c> picks
/// the prompt/schema/parser (default v4, the production default; v3 is the
/// shared contract).
///
/// Spend control: a JSON ledger (--ledger) accumulates the cost of every call
/// across runs; a receipt is skipped and the run stops when the ledger plus the
/// worst case of the next receipt (two full-budget calls) would pass --stop-at.
/// The key is read by the provider from ANTHROPIC_API_KEY only.
/// </summary>
internal static class AnthropicBenchmark
{
    private const decimal InputPricePerMillion = 0.10m;
    private const decimal OutputPricePerMillion = 0.50m;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static async Task<int> RunAsync(string repositoryRoot, IReadOnlyList<string> args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        AnthropicArguments arguments;
        try
        {
            arguments = AnthropicArguments.Parse(args, repositoryRoot);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine($"anthropic benchmark: {exception.Message}");
            Console.Error.WriteLine(AnthropicArguments.Usage);
            return 2;
        }

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(AnthropicStructuredVisionProvider.ApiKeyEnvironmentVariable)))
        {
            Console.Error.WriteLine($"anthropic benchmark: {AnthropicStructuredVisionProvider.ApiKeyEnvironmentVariable} is not set.");
            return 2;
        }

        var groundTruthPath = new[]
            {
                Path.Combine(arguments.DatasetDirectory, "results", "ground_truth.json"),
                Path.Combine(arguments.DatasetDirectory, "ground_truth.json")
            }
            .FirstOrDefault(File.Exists);
        var groundTruth = groundTruthPath is null
            ? new Dictionary<string, GroundTruth>()
            : CloudBenchmark.LoadGroundTruth(groundTruthPath);
        CloudBenchmark.ReceiptFile[] receipts;
        try
        {
            receipts = CloudBenchmark.ResolveReceipts(arguments.DatasetDirectory, arguments.Receipts);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine($"anthropic benchmark: {exception.Message}");
            return 2;
        }

        Directory.CreateDirectory(arguments.OutputDirectory);
        var options = new ReceiptRecognitionOptions
        {
            Pipeline = AnthropicStructuredVisionProvider.ProviderId,
            AnthropicModel = arguments.Model,
            AnthropicEffort = Enum.Parse<AnthropicEffort>(arguments.Effort, ignoreCase: true),
            AnthropicReceiptContract = arguments.Contract
        };
        using var provider = new AnthropicStructuredVisionProvider(
            Options.Create(options), NullLogger<AnthropicStructuredVisionProvider>.Instance);
        var counter = new UsageCountingVisionProvider(provider);
        var workflow = new ReceiptAnalysisWorkflow(new StructuredVisionProviderResolver([counter]));
        var worstCasePerReceipt = 2 * Cost(10_000, options.AnthropicMaximumOutputTokens);

        Console.WriteLine(
            $"{arguments.Label}: model {arguments.Model}, effort {arguments.Effort}, contract {arguments.Contract}; "
            + $"receipts {string.Join(",", receipts.Select(receipt => receipt.Id))}; output {arguments.OutputDirectory}");

        var results = new List<AnthropicReceiptResult>();
        string? stopReason = null;
        foreach (var receipt in receipts)
        {
            var ledger = Ledger.Read(arguments.LedgerPath);
            if (ledger.SpentUsd + worstCasePerReceipt > arguments.StopAtUsd)
            {
                stopReason = $"ledger ${ledger.SpentUsd:0.0000} + worst case ${worstCasePerReceipt:0.0000} would pass ${arguments.StopAtUsd:0.00}";
                break;
            }

            Console.Write($"{receipt.Id}: ");
            var image = await File.ReadAllBytesAsync(receipt.Path);
            var stopwatch = Stopwatch.StartNew();
            AnthropicReceiptResult result;
            try
            {
                var analysis = await workflow.AnalyzeAsync(
                    new ReceiptAnalysisRequest(
                        Guid.NewGuid(),
                        Guid.NewGuid().ToString("N"),
                        [new ReceiptImageData(image, "image/jpeg")],
                        counter.Id,
                        ContractVersion: options.AnthropicReceiptContract),
                    CancellationToken.None);
                stopwatch.Stop();
                var usage = counter.TakeUsage();
                var shape = CloudReceiptResult.Success(
                    arguments.Label, arguments.Model, receipt.Id, stopwatch.ElapsedMilliseconds, [], analysis.Extraction, groundTruth.GetValueOrDefault(receipt.Id));
                result = new AnthropicReceiptResult(
                    arguments.Label, arguments.Model, receipt.Id, "success", null, stopwatch.ElapsedMilliseconds, stopwatch.ElapsedMilliseconds / 1000d,
                    arguments.Effort, usage, Cost(usage.InputTokens, usage.OutputTokens),
                    shape.TotalMatches, BoxedScorePyParsed.From(analysis.Extraction), analysis.Extraction, analysis.Warnings.Count,
                    analysis.Extraction.Items.Count(item => item.BoundingBox is not null),
                    analysis.Extraction.ReceiptTotalBoundingBox is not null);
                Console.WriteLine(
                    $"ok {result.Seconds:0.0}s, tokens {usage.InputTokens}/{usage.OutputTokens} in {usage.Calls} call(s), ${result.CostUsd:0.00000}, "
                    + $"total {shape.Parsed?.Total?.ToString(CultureInfo.InvariantCulture) ?? "—"} ({(shape.TotalMatches == true ? "ok" : "MISMATCH")}), "
                    + $"{analysis.Extraction.Items.Count} items, boxes {result.ItemBoxes}/{analysis.Extraction.Items.Count}");
            }
            catch (Exception exception) when (exception is ReceiptExtractionException or ReceiptStructuredOutputException)
            {
                stopwatch.Stop();
                var usage = counter.TakeUsage();
                var status = exception is ReceiptStructuredOutputException ? "invalid-output" : "error";
                // A call that failed inside the provider (refusal, output limit) may
                // still be billed but reports no usage here: charge the worst case.
                var unreportedCalls = Math.Max(0, usage.Calls - usage.RequestIds.Count);
                result = new AnthropicReceiptResult(
                    arguments.Label, arguments.Model, receipt.Id, status, exception.Message, stopwatch.ElapsedMilliseconds, stopwatch.ElapsedMilliseconds / 1000d,
                    arguments.Effort, usage,
                    Cost(usage.InputTokens, usage.OutputTokens) + unreportedCalls * worstCasePerReceipt / 2,
                    null, null, null, 0, 0, false);
                Console.WriteLine($"{status} after {result.Seconds:0.0}s: {exception.Message}");
            }

            ledger.Add(arguments.Label, receipt.Id, result.CostUsd, result.Usage);
            Ledger.Write(arguments.LedgerPath, ledger);
            results.Add(result);
            await File.WriteAllTextAsync(
                Path.Combine(arguments.OutputDirectory, $"{arguments.Label}__{arguments.Model}__{receipt.Id}.json"),
                JsonSerializer.Serialize(result, JsonOptions));
        }

        var summary = CreateSummary(arguments, results, stopReason, Ledger.Read(arguments.LedgerPath).SpentUsd);
        await File.WriteAllTextAsync(Path.Combine(arguments.OutputDirectory, $"summary-{arguments.Label}.md"), summary);
        Console.WriteLine();
        Console.WriteLine(summary);
        return stopReason is null ? 0 : 3;
    }

    private static decimal Cost(long inputTokens, long outputTokens) =>
        decimal.Round((inputTokens * InputPricePerMillion + outputTokens * OutputPricePerMillion) / 1_000_000m, 6);

    private static string CreateSummary(
        AnthropicArguments arguments,
        List<AnthropicReceiptResult> results,
        string? stopReason,
        decimal ledgerTotal)
    {
        var successes = results.Where(result => result.Status == "success").ToArray();
        var seconds = successes.Select(result => result.Seconds).Order().ToArray();
        var builder = new StringBuilder();
        builder.AppendLine(CultureInfo.InvariantCulture, $"# {arguments.Label}: {arguments.Model}, effort {arguments.Effort}, contract {arguments.Contract}");
        if (stopReason is not null)
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"STOPPED: {stopReason}");
        }

        builder.AppendLine(CultureInfo.InvariantCulture, $"receipts {results.Count}, success {successes.Length}, total ok {successes.Count(result => result.TotalMatches == true)}");
        if (seconds.Length > 0)
        {
            builder.AppendLine(CultureInfo.InvariantCulture,
                $"latency median {seconds[seconds.Length / 2]:0.0}s, p90 {seconds[(int)Math.Min(seconds.Length - 1, Math.Ceiling(seconds.Length * 0.9) - 1)]:0.0}s, max {seconds[^1]:0.0}s");
        }

        builder.AppendLine(CultureInfo.InvariantCulture,
            $"calls {results.Sum(result => result.Usage.Calls)}, input tok {results.Sum(result => result.Usage.InputTokens)}, output tok {results.Sum(result => result.Usage.OutputTokens)}, "
            + $"cost ${results.Sum(result => result.CostUsd):0.0000} (${(results.Count == 0 ? 0 : results.Average(result => result.CostUsd)):0.00000}/receipt); ledger ${ledgerTotal:0.0000}");
        builder.AppendLine(CultureInfo.InvariantCulture,
            $"item boxes {successes.Sum(result => result.ItemBoxes)}/{successes.Sum(result => result.Extraction!.Items.Count)}, total boxes {successes.Count(result => result.TotalBox)}/{successes.Length}");
        return builder.ToString();
    }
}

internal sealed record AnthropicArguments(
    string DatasetDirectory,
    IReadOnlyList<string> Receipts,
    string OutputDirectory,
    string Label,
    string Model,
    string Effort,
    string LedgerPath,
    decimal StopAtUsd,
    ReceiptAnalysisContractVersion Contract)
{
    public const string Usage =
        "Usage: anthropic --out DIR --ledger FILE --label NAME [--dataset DIR] [--receipts 01,05] "
        + "[--model claude-haiku-5-5] [--effort low|medium|high] [--contract v4|v3] [--stop-at 0.45]";

    public static AnthropicArguments Parse(IReadOnlyList<string> args, string repositoryRoot)
    {
        var dataset = Path.Combine(repositoryRoot, "test-data", "receipts", "household-2026-08");
        string[] receipts = [];
        string? output = null;
        string? ledger = null;
        string? label = null;
        var model = "claude-haiku-5-5";
        var effort = "low";
        var stopAt = 0.45m;
        var contract = ReceiptAnalysisContractVersion.V4Structured;
        for (var index = 0; index < args.Count; index++)
        {
            switch (args[index])
            {
                case "--dataset": dataset = Path.GetFullPath(Value(args, ref index)); break;
                case "--receipts": receipts = Value(args, ref index).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries); break;
                case "--out": output = Path.GetFullPath(Value(args, ref index)); break;
                case "--ledger": ledger = Path.GetFullPath(Value(args, ref index)); break;
                case "--label": label = Value(args, ref index); break;
                case "--model": model = Value(args, ref index); break;
                case "--effort": effort = Value(args, ref index).ToLowerInvariant(); break;
                case "--contract":
                    contract = Value(args, ref index).ToLowerInvariant() switch
                    {
                        "v3" => ReceiptAnalysisContractVersion.V3,
                        "v4" => ReceiptAnalysisContractVersion.V4Structured,
                        _ => throw new ArgumentException("--contract must be v4 or v3.")
                    };
                    break;
                case "--stop-at": stopAt = decimal.Parse(Value(args, ref index), CultureInfo.InvariantCulture); break;
                default: throw new ArgumentException($"Unknown argument '{args[index]}'.");
            }
        }

        if (output is null || ledger is null || string.IsNullOrWhiteSpace(label))
        {
            throw new ArgumentException("--out, --ledger and --label are required.");
        }

        if (effort is not ("low" or "medium" or "high"))
        {
            throw new ArgumentException("--effort must be low, medium or high.");
        }

        if (!Directory.Exists(dataset))
        {
            throw new ArgumentException($"Dataset folder '{dataset}' does not exist.");
        }

        return new(dataset, receipts, output, label, model, effort, ledger, stopAt, contract);

        static string Value(IReadOnlyList<string> args, ref int index) =>
            ++index < args.Count ? args[index] : throw new ArgumentException($"{args[index - 1]} needs a value.");
    }
}

internal sealed record AnthropicReceiptResult(
    string Configuration,
    string Model,
    string ReceiptId,
    string Status,
    string? Error,
    long ElapsedMilliseconds,
    double Seconds,
    string Effort,
    OpenAiUsage Usage,
    decimal CostUsd,
    bool? TotalMatches,
    BoxedScorePyParsed? Parsed,
    ReceiptExtractionResult? Extraction,
    int Warnings,
    int ItemBoxes,
    bool TotalBox);

/// <summary>score_items.py's <c>parsed</c> shape plus boxes as ocr_compare reads them.</summary>
internal sealed record BoxedScorePyParsed(
    string? Merchant,
    string? Date,
    string? Currency,
    decimal? Total,
    double[]? TotalBox,
    IReadOnlyList<BoxedScorePyItem> Items)
{
    public static BoxedScorePyParsed From(ReceiptExtractionResult extraction) => new(
        extraction.MerchantName,
        extraction.PurchasedAt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        extraction.Currency,
        extraction.ReceiptTotal,
        ToArray(extraction.ReceiptTotalBoundingBox),
        extraction.Items
            .Select(item => new BoxedScorePyItem(item.Name, item.Quantity, item.UnitPrice, item.LineTotal, ToArray(item.BoundingBox)))
            .ToArray());

    private static double[]? ToArray(PurchaseMemory.Domain.Receipts.BoundingBox? box) =>
        box is null ? null : [box.X, box.Y, box.Width, box.Height];
}

internal sealed record BoxedScorePyItem(string Name, decimal? Quantity, decimal? UnitPrice, decimal? LineTotal, double[]? Box);

/// <summary>Cumulative spend across benchmark runs; the hard budget stop reads it.</summary>
internal sealed class Ledger
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public decimal SpentUsd { get; set; }

    public List<LedgerEntry> Calls { get; set; } = [];

    public void Add(string label, string receiptId, decimal costUsd, OpenAiUsage usage)
    {
        SpentUsd += costUsd;
        Calls.Add(new LedgerEntry(label, receiptId, costUsd, usage.Calls, usage.InputTokens, usage.OutputTokens));
    }

    public static Ledger Read(string path) =>
        File.Exists(path)
            ? JsonSerializer.Deserialize<Ledger>(File.ReadAllText(path), JsonOptions) ?? new Ledger()
            : new Ledger();

    public static void Write(string path, Ledger ledger)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(ledger, JsonOptions));
    }
}

internal sealed record LedgerEntry(string Label, string ReceiptId, decimal CostUsd, int Calls, long InputTokens, long OutputTokens);
