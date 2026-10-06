using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PurchaseMemory.Application.Receipts;
using PurchaseMemory.Infrastructure.Recognition;

var repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);
if (args.Length > 0 && string.Equals(args[0], "cloud", StringComparison.OrdinalIgnoreCase))
{
    // Direct calls to the Ollama HTTP API (hosted or local) on the household set;
    // see CloudBenchmark for the arguments.
    return await CloudBenchmark.RunAsync(repositoryRoot, args[1..]);
}

if (args.Length > 0 && string.Equals(args[0], "openai", StringComparison.OrdinalIgnoreCase))
{
    // Photo → OpenAI vision models on the household set; see OpenAiBenchmark.
    return await OpenAiBenchmark.RunAsync(repositoryRoot, args[1..]);
}

var dataDirectory = Path.Combine(repositoryRoot, "test-data", "receipts", "public");
var outputDirectory = Path.Combine(repositoryRoot, "test-data", "receipts", "benchmarks");
Directory.CreateDirectory(outputDirectory);

var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    WriteIndented = true
};
var expectedPath = Path.Combine(dataDirectory, "expected.json");
var expected = JsonSerializer.Deserialize<ExpectedDataset>(
    await File.ReadAllTextAsync(expectedPath),
    jsonOptions) ?? throw new InvalidOperationException("Cannot read the expected receipt dataset.");

var configurations = new[]
{
    BenchmarkConfiguration.Apple("apple-vision-translation-foundation"),
    BenchmarkConfiguration.Hybrid("hybrid-glm-qwen4", "glm-ocr:latest", "qwen3.5:4b"),
    BenchmarkConfiguration.Vision("vision-qwen4", "qwen3.5:4b"),
    BenchmarkConfiguration.Vision("vision-qwen9", "qwen3.5:9b"),
    BenchmarkConfiguration.Vision("vision-gemma4-12b", "gemma4:12b-mlx"),
    BenchmarkConfiguration.Vision("vision-minicpm-v4.6", "minicpm-v4.6"),
    BenchmarkConfiguration.AppleRows("apple-rows-qwen35", "qwen3.5:35b")
};

var reportPath = Path.Combine(outputDirectory, "latest.json");
var markdownPath = Path.Combine(outputDirectory, "latest.md");
if (args.Contains("--rescore", StringComparer.OrdinalIgnoreCase))
{
    var previous = JsonSerializer.Deserialize<BenchmarkReport>(
        await File.ReadAllTextAsync(reportPath),
        jsonOptions) ?? throw new InvalidOperationException("Cannot read the previous benchmark report.");
    var rescored = previous.Results.Select(result =>
    {
        var groundTruth = expected.Receipts.Single(receipt => receipt.Id == result.ReceiptId);
        return result.Extraction is null
            ? result with { Score = null }
            : result with { Score = Score(groundTruth, result.Extraction) };
    }).ToList();
    var updated = previous with { GeneratedAt = DateTimeOffset.Now, Results = rescored };
    await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(updated, jsonOptions));
    await File.WriteAllTextAsync(markdownPath, CreateMarkdownReport(updated, configurations));
    Console.WriteLine($"Rescored results: {markdownPath}");
    return 0;
}

var mergeWithPrevious = args.Contains("--merge", StringComparer.OrdinalIgnoreCase);
var selectedIds = args
    .Where(argument => !argument.StartsWith("--", StringComparison.Ordinal))
    .ToHashSet(StringComparer.OrdinalIgnoreCase);
var selectedConfigurations = selectedIds.Count == 0
    ? configurations
    : configurations.Where(configuration => selectedIds.Contains(configuration.Id)).ToArray();

if (selectedConfigurations.Length == 0)
{
    var available = string.Join(", ", configurations.Select(configuration => configuration.Id));
    throw new ArgumentException($"No benchmark configuration was selected. Available values: {available}");
}

// Vision and hybrid routes share one client; the apple-ollama route gets its own
// inside CreateExtractor, because HttpClient.Timeout is fixed once the client has
// been used and that route's ceiling comes from RowsParserRequestTimeoutSeconds.
using var httpClient = new HttpClient
{
    BaseAddress = new Uri("http://127.0.0.1:11434/", UriKind.Absolute),
    Timeout = TimeSpan.FromMinutes(10)
};

var results = new List<BenchmarkResult>();
// Providers that own an OS resource are kept until the whole sweep is over:
// one configuration's extractor must not be torn down while another still runs.
var pipelineDisposables = new List<IDisposable>();
foreach (var configuration in selectedConfigurations)
{
    Console.WriteLine($"\n## {configuration.Id}");
    var extractor = CreateExtractor(httpClient, configuration, repositoryRoot, pipelineDisposables);
    var status = await extractor.GetStatusAsync(CancellationToken.None);
    if (!status.Available)
    {
        Console.WriteLine($"SKIP: {status.Message}");
        foreach (var receipt in expected.Receipts)
        {
            results.Add(BenchmarkResult.Skipped(configuration.Id, receipt.Id, status.Message));
        }

        continue;
    }

    foreach (var receipt in expected.Receipts)
    {
        Console.Write($"{receipt.Id}: ");
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var extraction = await extractor.ExtractAsync(
                [new ReceiptImageData(
                    await File.ReadAllBytesAsync(Path.Combine(dataDirectory, receipt.File)),
                    ContentTypeFor(receipt.File))],
                CancellationToken.None);
            stopwatch.Stop();

            var score = Score(receipt, extraction);
            var result = BenchmarkResult.Success(
                configuration.Id,
                receipt.Id,
                stopwatch.Elapsed,
                score,
                extraction);
            results.Add(result);
            Console.WriteLine($"{score.Total:0}/100, {stopwatch.Elapsed.TotalSeconds:0.0}s, {extraction.Items.Count} items");
        }
        catch (Exception exception) when (exception is ReceiptExtractionException or HttpRequestException or TaskCanceledException)
        {
            stopwatch.Stop();
            var diagnostic = exception.InnerException is null
                ? exception.Message
                : $"{exception.Message} ({exception.InnerException.Message})";
            results.Add(BenchmarkResult.Failed(
                configuration.Id,
                receipt.Id,
                stopwatch.Elapsed,
                diagnostic));
            Console.WriteLine($"FAIL after {stopwatch.Elapsed.TotalSeconds:0.0}s: {diagnostic}");
        }
    }
}

foreach (var disposable in pipelineDisposables)
{
    disposable.Dispose();
}

if (mergeWithPrevious && File.Exists(reportPath))
{
    var previous = JsonSerializer.Deserialize<BenchmarkReport>(
        await File.ReadAllTextAsync(reportPath),
        jsonOptions);
    if (previous is not null)
    {
        var replacedConfigurations = selectedConfigurations
            .Select(configuration => configuration.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        results.InsertRange(
            0,
            previous.Results.Where(result => !replacedConfigurations.Contains(result.ConfigurationId)));
    }
}

var generatedAt = DateTimeOffset.Now;
var report = new BenchmarkReport(
    generatedAt,
    Environment.MachineName,
    expected.SchemaVersion,
    results);
await File.WriteAllTextAsync(
    reportPath,
    JsonSerializer.Serialize(report, jsonOptions));
await File.WriteAllTextAsync(
    markdownPath,
    CreateMarkdownReport(report, mergeWithPrevious ? configurations : selectedConfigurations));

Console.WriteLine($"\nResults: {markdownPath}");
return 0;

static IReceiptRecognitionPipeline CreateExtractor(
    HttpClient client,
    BenchmarkConfiguration configuration,
    string repositoryRoot,
    List<IDisposable> disposables)
{
    var options = new ReceiptRecognitionOptions
    {
        Pipeline = configuration.PipelineId,
        Mode = configuration.Mode,
        OcrModel = configuration.OcrModel ?? "glm-ocr:latest",
        ParserModel = configuration.ParserModel ?? "qwen3.5:4b",
        VisionModel = configuration.VisionModel ?? "qwen3-vl:8b-instruct",
        RowsParserModel = configuration.RowsParserModel ?? "qwen3.5:35b",
        AppleSidecarPath = Path.Combine(
            repositoryRoot,
            ".artifacts",
            "apple-intelligence",
            "PurchaseMemory.AppleIntelligence")
    };

    // The benchmark builds options by hand instead of going through the
    // options pipeline, so run the production validator explicitly: otherwise
    // an invalid combination (for example an output budget that does not fit
    // inside the context window) would silently skew the model comparison.
    var descriptors = new[] { options.Pipeline }
        .Concat(options.FallbackPipelines)
        .Select(id => new ReceiptRecognitionPipelineDescriptor(id));
    var validation = new ReceiptRecognitionOptionsValidator(descriptors)
        .Validate(null, options);
    if (validation.Failed)
    {
        throw new InvalidOperationException(
            $"Benchmark configuration '{configuration.Id}' (pipeline '{configuration.PipelineId}') "
            + "is invalid: "
            + string.Join("; ", validation.Failures ?? []));
    }

    if (string.Equals(configuration.PipelineId, "apple", StringComparison.OrdinalIgnoreCase))
    {
        return new AppleReceiptExtractor(
            Options.Create(options),
            NullLogger<AppleReceiptExtractor>.Instance);
    }

    if (string.Equals(
            configuration.PipelineId,
            AppleGeometryOllamaExtractor.PipelineId,
            StringComparison.OrdinalIgnoreCase))
    {
        // The benchmark must wait exactly as long as production does: a cloud tag or
        // a larger local model that runs past the shared 10-minute ceiling would be
        // recorded as a model failure when it is really a limit of this tool.
        var textClient = new HttpClient
        {
            BaseAddress = client.BaseAddress,
            Timeout = TimeSpan.FromSeconds(options.RowsParserRequestTimeoutSeconds)
        };
        disposables.Add(textClient);
        var textProvider = new OllamaStructuredTextProvider(
            textClient,
            Options.Create(options),
            NullLogger<OllamaStructuredTextProvider>.Instance);
        disposables.Add(textProvider);
        var textResolver = new StructuredTextProviderResolver([textProvider]);
        return new AppleGeometryOllamaExtractor(
            new AppleReceiptGeometryReader(
                Options.Create(options),
                NullLogger<AppleReceiptGeometryReader>.Instance),
            new ReceiptRowsAnalysisWorkflow(textResolver),
            textResolver,
            NullLogger<AppleGeometryOllamaExtractor>.Instance);
    }

    var provider = new OllamaStructuredVisionProvider(
        client,
        Options.Create(options),
        NullLogger<OllamaStructuredVisionProvider>.Instance);
    disposables.Add(provider);
    var resolver = new StructuredVisionProviderResolver([provider]);
    return new OllamaReceiptExtractor(
        new ReceiptAnalysisWorkflow(resolver),
        resolver,
        NullLogger<OllamaReceiptExtractor>.Instance);
}

static BenchmarkScore Score(ExpectedReceipt expected, ReceiptExtractionResult actual)
{
    var merchant = actual.MerchantName is not null
        && expected.MerchantAliases.Any(alias =>
            Normalize(actual.MerchantName).Contains(Normalize(alias), StringComparison.Ordinal));
    var date = actual.PurchasedAt is not null
        && DateOnly.FromDateTime(actual.PurchasedAt.Value.Date) == expected.PurchasedOn;
    var total = actual.ReceiptTotal is not null
        && Math.Abs(actual.ReceiptTotal.Value - expected.ReceiptTotal) <= 0.01m;
    var countDifference = Math.Abs(actual.Items.Count - expected.ItemCount);
    var itemCountPoints = countDifference switch
    {
        0 => 15,
        1 => 10,
        2 => 5,
        _ => 0
    };
    var hasCompleteLineTotals = actual.Items.Count > 0
        && actual.Items.All(item => item.LineTotal is not null);
    var lineTotalSum = hasCompleteLineTotals
        && Math.Abs(actual.Items.Sum(item => item.LineTotal!.Value) - expected.LineTotalSum) <= 0.01m;
    var matchedNames = expected.ItemNameFragments.Count(fragment => actual.Items.Any(item =>
        Normalize(item.Name).Contains(Normalize(fragment), StringComparison.Ordinal)));
    var itemNamePoints = expected.ItemNameFragments.Count == 0
        ? 15
        : (int)Math.Round(15d * matchedNames / expected.ItemNameFragments.Count, MidpointRounding.AwayFromZero);

    return new BenchmarkScore(
        merchant ? 20 : 0,
        date ? 15 : 0,
        total ? 20 : 0,
        itemCountPoints,
        lineTotalSum ? 15 : 0,
        itemNamePoints);
}

static string Normalize(string value)
{
    var decomposed = value.Normalize(NormalizationForm.FormD);
    var builder = new StringBuilder(decomposed.Length);
    foreach (var character in decomposed)
    {
        if (char.IsLetterOrDigit(character))
        {
            builder.Append(char.ToLowerInvariant(character));
        }
    }

    return builder.ToString();
}

static string ContentTypeFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
{
    ".jpg" or ".jpeg" => "image/jpeg",
    ".png" => "image/png",
    ".webp" => "image/webp",
    _ => throw new NotSupportedException($"Unsupported image file: {path}")
};

static string FindRepositoryRoot(string startDirectory)
{
    var directory = new DirectoryInfo(startDirectory);
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "PurchaseMemory.slnx")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    throw new DirectoryNotFoundException("Cannot locate the PurchaseMemory repository root.");
}

static string CreateMarkdownReport(
    BenchmarkReport report,
    IReadOnlyCollection<BenchmarkConfiguration> configurations)
{
    var builder = new StringBuilder();
    builder.AppendLine("# Receipt recognition benchmark");
    builder.AppendLine();
    builder.Append("Generated: ")
        .Append(report.GeneratedAt.ToString("O", CultureInfo.InvariantCulture))
        .Append(" on `")
        .Append(report.MachineName)
        .AppendLine("`.");
    builder.AppendLine();
    builder.AppendLine("> This is a small viability benchmark, not a statistically representative accuracy study. " +
        "Latency includes model loading and therefore reflects the local prototype experience.");
    builder.AppendLine();
    builder.AppendLine("| Pipeline | Successful | Mean score | Mean latency | Mean items |");
    builder.AppendLine("|---|---:|---:|---:|---:|");

    foreach (var configuration in configurations)
    {
        var completed = report.Results
            .Where(result => result.ConfigurationId == configuration.Id && result.Status == "success")
            .ToArray();
        builder.Append("| ").Append(configuration.Id).Append(" | ").Append(completed.Length).Append('/').Append(3);
        if (completed.Length == 0)
        {
            builder.AppendLine(" | — | — | — |");
            continue;
        }

        builder.Append(" | ")
            .Append(completed.Average(result => result.Score?.Total ?? 0).ToString("0.0", CultureInfo.InvariantCulture))
            .Append(" | ")
            .Append(completed.Average(result => result.ElapsedMilliseconds) / 1000d is var seconds
                ? seconds.ToString("0.0", CultureInfo.InvariantCulture) + "s"
                : "—")
            .Append(" | ")
            .Append(completed.Average(result => result.Extraction?.Items.Count ?? 0).ToString("0.0", CultureInfo.InvariantCulture))
            .AppendLine(" |");
    }

    builder.AppendLine();
    builder.AppendLine("## Individual runs");
    builder.AppendLine();
    builder.AppendLine("| Pipeline | Receipt | Status | Score | Time | Merchant | Date | Total | Items |");
    builder.AppendLine("|---|---|---|---:|---:|---|---|---:|---:|");
    foreach (var result in report.Results)
    {
        builder.Append("| ").Append(result.ConfigurationId)
            .Append(" | ").Append(result.ReceiptId)
            .Append(" | ").Append(result.Status)
            .Append(" | ").Append(result.Score?.Total.ToString(CultureInfo.InvariantCulture) ?? "—")
            .Append(" | ").Append((result.ElapsedMilliseconds / 1000d).ToString("0.0", CultureInfo.InvariantCulture)).Append('s')
            .Append(" | ").Append(Escape(result.Extraction?.MerchantName))
            .Append(" | ").Append(result.Extraction?.PurchasedAt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "—")
            .Append(" | ").Append(result.Extraction?.ReceiptTotal?.ToString("0.00", CultureInfo.InvariantCulture) ?? "—")
            .Append(" | ").Append(result.Extraction?.Items.Count.ToString(CultureInfo.InvariantCulture) ?? "—")
            .AppendLine(" |");
    }

    builder.AppendLine();
    builder.AppendLine("The JSON file beside this report contains every extracted line and error message.");
    return builder.ToString();

    static string Escape(string? value) => string.IsNullOrWhiteSpace(value)
        ? "—"
        : value.Replace("|", "\\|", StringComparison.Ordinal);
}

internal sealed record ExpectedDataset(int SchemaVersion, IReadOnlyList<ExpectedReceipt> Receipts);

internal sealed record ExpectedReceipt(
    string Id,
    string File,
    IReadOnlyList<string> MerchantAliases,
    DateOnly PurchasedOn,
    decimal ReceiptTotal,
    int ItemCount,
    decimal LineTotalSum,
    IReadOnlyList<string> ItemNameFragments);

internal sealed record BenchmarkConfiguration(
    string Id,
    string PipelineId,
    ReceiptRecognitionMode Mode,
    string? OcrModel,
    string? ParserModel,
    string? VisionModel,
    string? RowsParserModel = null)
{
    public static BenchmarkConfiguration Apple(string id) =>
        new(id, "apple", ReceiptRecognitionMode.Hybrid, null, null, null);

    public static BenchmarkConfiguration Hybrid(string id, string ocrModel, string parserModel) =>
        new(id, "ollama", ReceiptRecognitionMode.Hybrid, ocrModel, parserModel, null);

    public static BenchmarkConfiguration Vision(string id, string visionModel) =>
        new(id, "ollama", ReceiptRecognitionMode.Vision, null, null, visionModel);

    /// <summary>Apple Vision OCR rows structured by a configurable text model.</summary>
    public static BenchmarkConfiguration AppleRows(string id, string rowsParserModel) =>
        new(
            id,
            AppleGeometryOllamaExtractor.PipelineId,
            ReceiptRecognitionMode.Vision,
            null,
            null,
            null,
            rowsParserModel);
}

internal sealed record BenchmarkScore(
    int MerchantPoints,
    int DatePoints,
    int TotalPoints,
    int ItemCountPoints,
    int LineTotalSumPoints,
    int ItemNamePoints)
{
    public int Total =>
        MerchantPoints + DatePoints + TotalPoints + ItemCountPoints + LineTotalSumPoints + ItemNamePoints;
}

internal sealed record BenchmarkResult(
    string ConfigurationId,
    string ReceiptId,
    string Status,
    long ElapsedMilliseconds,
    BenchmarkScore? Score,
    ReceiptExtractionResult? Extraction,
    string? Error)
{
    public static BenchmarkResult Success(
        string configurationId,
        string receiptId,
        TimeSpan elapsed,
        BenchmarkScore score,
        ReceiptExtractionResult extraction) =>
        new(configurationId, receiptId, "success", (long)elapsed.TotalMilliseconds, score, extraction, null);

    public static BenchmarkResult Failed(
        string configurationId,
        string receiptId,
        TimeSpan elapsed,
        string error) =>
        new(configurationId, receiptId, "failed", (long)elapsed.TotalMilliseconds, null, null, error);

    public static BenchmarkResult Skipped(string configurationId, string receiptId, string? error) =>
        new(configurationId, receiptId, "skipped", 0, null, null, error);
}

internal sealed record BenchmarkReport(
    DateTimeOffset GeneratedAt,
    string MachineName,
    int DatasetSchemaVersion,
    IReadOnlyList<BenchmarkResult> Results);
