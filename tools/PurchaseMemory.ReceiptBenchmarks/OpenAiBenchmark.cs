using System.ClientModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PurchaseMemory.Application.Receipts;
using PurchaseMemory.Infrastructure.Recognition;

/// <summary>
/// <c>openai</c> sub-command: photo → OpenAI vision model on the household receipt
/// set, through the production OpenAiStructuredVisionProvider and the same
/// ReceiptAnalysisWorkflow (prompt, schema, repair retry) as the photo pipeline.
/// Writes one JSON per receipt in the cloud command's format (score_cloud.py and
/// score_items.py read both) plus usage tokens and the cost at list prices.
/// The API key is read by the provider from OPENAI_API_KEY only.
/// </summary>
internal static class OpenAiBenchmark
{
    public const string Configuration = "openai-photo";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static async Task<int> RunAsync(string repositoryRoot, IReadOnlyList<string> args)
    {
        // Seconds and dollars are printed with a decimal point whatever the machine locale.
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        OpenAiArguments arguments;
        try
        {
            arguments = OpenAiArguments.Parse(args);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine($"openai benchmark: {exception.Message}");
            Console.Error.WriteLine(OpenAiArguments.Usage);
            return 2;
        }

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(OpenAiStructuredVisionProvider.ApiKeyEnvironmentVariable)))
        {
            Console.Error.WriteLine($"openai benchmark: {OpenAiStructuredVisionProvider.ApiKeyEnvironmentVariable} is not set.");
            return 2;
        }

        var datasetDirectory = Path.Combine(repositoryRoot, "test-data", "receipts", "household-2026-08");
        var groundTruth = CloudBenchmark.LoadGroundTruth(Path.Combine(datasetDirectory, "results", "ground_truth.json"));
        CloudBenchmark.ReceiptFile[] receipts;
        try
        {
            receipts = CloudBenchmark.ResolveReceipts(datasetDirectory, arguments.Receipts);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine($"openai benchmark: {exception.Message}");
            return 2;
        }

        var outputDirectory = arguments.OutputDirectory ?? Path.Combine(
            repositoryRoot, "test-data", "receipts", "benchmarks", "openai", $"{DateTimeOffset.Now:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(outputDirectory);
        Console.WriteLine($"Models: {string.Join(", ", arguments.Models)}; receipts: {string.Join(", ", receipts.Select(r => r.Id))}; detail {arguments.ImageDetail}; reasoning {arguments.ReasoningEffort}");
        Console.WriteLine($"Output: {outputDirectory}");

        var results = new List<OpenAiReceiptResult>();
        string? stopReason = null;
        // Sequential on purpose: account limits are shared and parallel calls would distort timings.
        foreach (var model in arguments.Models)
        {
            Console.WriteLine($"\n## {Configuration} / {model}");
            var options = new ReceiptRecognitionOptions
            {
                Pipeline = OpenAiStructuredVisionProvider.ProviderId,
                OpenAiModel = model,
                OpenAiImageDetail = Enum.Parse<OpenAiImageDetail>(arguments.ImageDetail, ignoreCase: true),
                OpenAiReasoningEffort = Enum.Parse<OpenAiReasoningEffort>(arguments.ReasoningEffort, ignoreCase: true)
            };
            using var provider = new OpenAiStructuredVisionProvider(Options.Create(options), NullLogger<OpenAiStructuredVisionProvider>.Instance);
            var counter = new UsageCountingVisionProvider(provider);
            var workflow = new ReceiptAnalysisWorkflow(new StructuredVisionProviderResolver([counter]));

            foreach (var receipt in receipts)
            {
                Console.Write($"{receipt.Id}: ");
                var image = await File.ReadAllBytesAsync(receipt.Path);
                var stopwatch = Stopwatch.StartNew();
                OpenAiReceiptResult result;
                int? httpStatus = null;
                try
                {
                    var analysis = await workflow.AnalyzeAsync(
                        new ReceiptAnalysisRequest(Guid.NewGuid(), Guid.NewGuid().ToString("N"), [new ReceiptImageData(image, "image/jpeg")], counter.Id),
                        CancellationToken.None);
                    stopwatch.Stop();
                    var usage = counter.TakeUsage();
                    var cloudShape = CloudReceiptResult.Success(
                        Configuration, model, receipt.Id, stopwatch.ElapsedMilliseconds, [], analysis.Extraction, groundTruth.GetValueOrDefault(receipt.Id));
                    result = new OpenAiReceiptResult(
                        Configuration, model, receipt.Id, "success", null, stopwatch.ElapsedMilliseconds, stopwatch.ElapsedMilliseconds / 1000d,
                        arguments.ImageDetail, arguments.ReasoningEffort, usage, usage.CostUsd(model),
                        cloudShape.TotalMatches, cloudShape.Parsed, analysis.Extraction, analysis.Warnings.Count, ModelInput.Of("image", image));
                    Console.WriteLine(
                        $"ok, {stopwatch.Elapsed.TotalSeconds:0.0}s, tokens {usage.InputTokens}/{usage.OutputTokens} in {usage.Calls} call(s), "
                        + $"{OpenAiPrices.Describe(result.CostUsd)}, total {result.Parsed?.Total?.ToString(CultureInfo.InvariantCulture) ?? "—"}, {result.Parsed?.Items.Count ?? 0} items");
                }
                catch (Exception exception) when (exception is ReceiptExtractionException or ReceiptStructuredOutputException or HttpRequestException)
                {
                    stopwatch.Stop();
                    var usage = counter.TakeUsage();
                    httpStatus = FindHttpStatus(exception);
                    var status = exception is ReceiptStructuredOutputException ? "invalid-output"
                        : httpStatus is { } code ? $"http-{code}" : "error";
                    // Only the provider's own safe message: it never contains the key or the request.
                    result = new OpenAiReceiptResult(
                        Configuration, model, receipt.Id, status, exception.Message, stopwatch.ElapsedMilliseconds, stopwatch.ElapsedMilliseconds / 1000d,
                        arguments.ImageDetail, arguments.ReasoningEffort, usage, usage.CostUsd(model), null, null, null, 0, ModelInput.Of("image", image));
                    Console.WriteLine($"{status} after {stopwatch.Elapsed.TotalSeconds:0.0}s: {exception.Message}");
                }

                results.Add(result);
                await File.WriteAllTextAsync(
                    Path.Combine(outputDirectory, $"{Configuration}__{SafeFileName(model)}__{receipt.Id}.json"),
                    JsonSerializer.Serialize(result, JsonOptions));
                if (httpStatus is 401 or 403 or 429)
                {
                    stopReason = $"HTTP {httpStatus} on model {model}, receipt {receipt.Id}";
                    break;
                }

                if (httpStatus is 404)
                {
                    Console.WriteLine($"Model {model} is not available (HTTP 404); skipping its remaining receipts.");
                    break;
                }
            }

            if (stopReason is not null)
            {
                break;
            }
        }

        if (stopReason is not null)
        {
            Console.WriteLine($"\nSTOPPED: {stopReason}. Remaining runs were not attempted.");
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "summary-openai.md"), CreateMarkdown(arguments.Models, results, stopReason));
        Console.WriteLine($"\nSummary: {Path.Combine(outputDirectory, "summary-openai.md")}");
        return stopReason is null ? 0 : 3;
    }

    /// <summary>The HTTP status the OpenAI SDK attached to the failure, if any.</summary>
    internal static int? FindHttpStatus(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is ClientResultException { Status: > 0 } clientResult)
            {
                return clientResult.Status;
            }
        }

        return null;
    }

    private static string SafeFileName(string value) =>
        new(value.Select(character => char.IsLetterOrDigit(character) || character is '.' or '-' or '_' ? character : '_').ToArray());

    private static string CreateMarkdown(IReadOnlyList<string> models, IReadOnlyList<OpenAiReceiptResult> results, string? stopReason)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# OpenAI photo benchmark").AppendLine();
        if (stopReason is not null)
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"**Stopped early:** {stopReason}.").AppendLine();
        }

        builder.AppendLine("| Model | Receipts | Success | Total ±0.01 | Mean time | Mean input tok | Mean output tok | Cost total |");
        builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var model in models)
        {
            var rows = results.Where(result => result.Model == model).ToArray();
            if (rows.Length == 0)
            {
                continue;
            }

            var successes = rows.Where(result => result.Status == "success").ToArray();
            var cost = rows.All(result => result.CostUsd is not null) ? rows.Sum(result => result.CostUsd!.Value) : (decimal?)null;
            builder.Append(CultureInfo.InvariantCulture, $"| {model} | {rows.Length} | {successes.Length} | {successes.Count(result => result.TotalMatches == true)} | ")
                .Append(successes.Length == 0 ? "—" : $"{successes.Average(result => result.Seconds):0.0}s").Append(" | ")
                .Append(CultureInfo.InvariantCulture, $"{rows.Average(result => result.Usage.InputTokens):0} | {rows.Average(result => result.Usage.OutputTokens):0} | ")
                .Append(OpenAiPrices.Describe(cost)).AppendLine(" |");
        }

        return builder.ToString();
    }
}

internal sealed record OpenAiReceiptResult(
    string Configuration,
    string Model,
    string ReceiptId,
    string Status,
    string? Error,
    long ElapsedMilliseconds,
    double Seconds,
    string ImageDetail,
    string ReasoningEffort,
    OpenAiUsage Usage,
    decimal? CostUsd,
    bool? TotalMatches,
    ScorePyParsed? Parsed,
    ReceiptExtractionResult? Extraction,
    int Warnings,
    ModelInput Input);
