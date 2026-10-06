using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PurchaseMemory.Application.Receipts;
using PurchaseMemory.Infrastructure.Recognition;

/// <summary>
/// <c>cloud</c> sub-command: compares "photo to a cloud vision model" (cloud-photo)
/// with "Apple OCR rows to a cloud text model" (apple-rows-cloud) against the
/// household receipt set, calling the Ollama HTTP API directly the way a hosted
/// deployment would. Prompts, schemas and limits are the production ones: the
/// extractors are the same classes the API uses, only the endpoint differs.
///
/// Usage:
///   cloud --config cloud-photo|apple-rows-cloud --models m1,m2
///         [--receipts 01,05] [--base-url https://ollama.com] [--out DIR]
///         [--skip-model-check] [--continue-on-limit]
/// The API key is read from OLLAMA_API_KEY only.
/// </summary>
internal static class CloudBenchmark
{
    public const string CloudPhoto = "cloud-photo";
    public const string AppleRowsCloud = "apple-rows-cloud";
    public const string DefaultBaseUrl = "https://ollama.com";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static async Task<int> RunAsync(string repositoryRoot, IReadOnlyList<string> args)
    {
        CloudArguments arguments;
        OllamaEndpoint endpoint;
        try
        {
            arguments = CloudArguments.Parse(args);
            // Validated before anything else so a missing key fails before any request.
            endpoint = OllamaEndpoint.Create(
                arguments.BaseUrl,
                Environment.GetEnvironmentVariable(OllamaEndpoint.ApiKeyEnvironmentVariable));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            Console.Error.WriteLine($"cloud benchmark: {exception.Message}");
            Console.Error.WriteLine(CloudArguments.Usage);
            return 2;
        }

        var datasetDirectory = Path.Combine(repositoryRoot, "test-data", "receipts", "household-2026-08");
        var groundTruth = LoadGroundTruth(Path.Combine(datasetDirectory, "results", "ground_truth.json"));
        var receipts = ResolveReceipts(datasetDirectory, arguments.Receipts);
        var outputDirectory = arguments.OutputDirectory ?? Path.Combine(
            repositoryRoot,
            "test-data",
            "receipts",
            "benchmarks",
            "cloud",
            $"{DateTimeOffset.Now:yyyyMMdd-HHmmss}-{arguments.Configuration}");
        Directory.CreateDirectory(outputDirectory);

        Console.WriteLine($"Endpoint: {endpoint}");
        Console.WriteLine($"Configuration: {arguments.Configuration}; models: {string.Join(", ", arguments.Models)}; receipts: {string.Join(", ", receipts.Select(r => r.Id))}");
        Console.WriteLine($"Output: {outputDirectory}");

        // One OCR pass per receipt for the whole run: every text model must parse the
        // very same rows, otherwise a model comparison would partly measure OCR noise.
        var rowReader = arguments.Configuration == AppleRowsCloud
            ? new CachingRowReader(
                new AppleReceiptGeometryReader(
                    Options.Create(new ReceiptRecognitionOptions { AppleSidecarPath = SidecarPath(repositoryRoot) }),
                    NullLogger<AppleReceiptGeometryReader>.Instance),
                Path.Combine(outputDirectory, "ocr-rows"))
            : null;
        var results = new List<CloudReceiptResult>();
        var stopReason = (string?)null;
        // Models run one after another and receipts one at a time: the hosted API's
        // limits are per account, and parallel calls would distort the timings.
        foreach (var model in arguments.Models)
        {
            Console.WriteLine($"\n## {arguments.Configuration} / {model}");
            var recorder = new OllamaRequestRecorder { InnerHandler = new HttpClientHandler() };
            using var run = CreateExtractor(arguments, endpoint, model, recorder, repositoryRoot, rowReader);

            var status = await run.Extractor.GetStatusAsync(CancellationToken.None);
            var statusRecords = recorder.TakeRecords();
            if (!status.Available)
            {
                var kind = OllamaFailureClassifier.Classify(statusRecords, new InvalidOperationException(status.Message));
                Console.WriteLine($"SKIP ({Status(kind)}): {status.Message}");
                foreach (var receipt in receipts)
                {
                    var skipped = CloudReceiptResult.Failed(
                        arguments.Configuration, model, receipt.Id, "skipped-" + Status(kind), status.Message, 0, statusRecords);
                    results.Add(skipped);
                    await WriteResultAsync(outputDirectory, skipped);
                }

                if (ShouldStop(kind, arguments))
                {
                    stopReason = $"{Status(kind)} while checking model {model}";
                    break;
                }

                continue;
            }

            foreach (var receipt in receipts)
            {
                Console.Write($"{receipt.Id}: ");
                var image = await File.ReadAllBytesAsync(receipt.Path);
                rowReader?.ForgetLastInput();
                var stopwatch = Stopwatch.StartNew();
                CloudReceiptResult result;
                OllamaFailureKind failure = OllamaFailureKind.None;
                try
                {
                    var extraction = await run.Extractor.ExtractAsync(
                        [new ReceiptImageData(image, "image/jpeg")],
                        CancellationToken.None);
                    stopwatch.Stop();
                    result = CloudReceiptResult.Success(
                        arguments.Configuration,
                        model,
                        receipt.Id,
                        stopwatch.ElapsedMilliseconds,
                        recorder.TakeRecords(),
                        extraction,
                        groundTruth.GetValueOrDefault(receipt.Id)) with { Input = DescribeInput(image, rowReader) };
                    Console.WriteLine(
                        $"ok, {stopwatch.Elapsed.TotalSeconds:0.0}s, {result.RequestBytes} B sent, "
                        + $"total {result.Parsed?.Total?.ToString(CultureInfo.InvariantCulture) ?? "—"}, {result.Parsed?.Items.Count ?? 0} items");
                }
                catch (Exception exception) when (exception is ReceiptExtractionException or HttpRequestException or TaskCanceledException)
                {
                    stopwatch.Stop();
                    var records = recorder.TakeRecords();
                    failure = OllamaFailureClassifier.Classify(records, exception);
                    var diagnostic = exception.InnerException is null
                        ? exception.Message
                        : $"{exception.Message} ({exception.InnerException.Message})";
                    result = CloudReceiptResult.Failed(
                        arguments.Configuration, model, receipt.Id, Status(failure), diagnostic, stopwatch.ElapsedMilliseconds, records)
                        with { Input = DescribeInput(image, rowReader) };
                    Console.WriteLine($"{Status(failure)} after {stopwatch.Elapsed.TotalSeconds:0.0}s: {diagnostic}");
                }

                results.Add(result);
                await WriteResultAsync(outputDirectory, result);
                if (ShouldStop(failure, arguments))
                {
                    stopReason = $"{Status(failure)} on model {model}, receipt {receipt.Id}";
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

        var summary = new CloudSummary(
            DateTimeOffset.Now,
            arguments.Configuration,
            endpoint.BaseAddress.ToString(),
            arguments.Models,
            receipts.Select(receipt => receipt.Id).ToArray(),
            stopReason,
            results);
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "summary.json"), JsonSerializer.Serialize(summary, JsonOptions));
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "summary.md"), CreateMarkdown(summary, groundTruth));
        Console.WriteLine($"\nSummary: {Path.Combine(outputDirectory, "summary.md")}");
        return stopReason is null ? 0 : 3;
    }

    /// <summary>
    /// What the model was given: the photo for cloud-photo, the serialized OCR rows for
    /// apple-rows-cloud. Equal hashes across models prove they received the same input.
    /// </summary>
    private static ModelInput? DescribeInput(byte[] image, CachingRowReader? rowReader) =>
        rowReader is null ? ModelInput.Of("image", image) : rowReader.LastInput;

    private static string SidecarPath(string repositoryRoot) =>
        Path.Combine(repositoryRoot, ".artifacts", "apple-intelligence", "PurchaseMemory.AppleIntelligence");

    /// <summary>A rate limit or a bad key affects every later request as well.</summary>
    private static bool ShouldStop(OllamaFailureKind kind, CloudArguments arguments) =>
        kind == OllamaFailureKind.Unauthorized
        || (kind == OllamaFailureKind.RateLimited && !arguments.ContinueOnLimit);

    internal static string Status(OllamaFailureKind kind) => kind switch
    {
        OllamaFailureKind.None => "success",
        OllamaFailureKind.RateLimited => "rate-limited",
        OllamaFailureKind.Unauthorized => "unauthorized",
        OllamaFailureKind.HttpError => "http-error",
        OllamaFailureKind.Unreachable => "unreachable",
        OllamaFailureKind.Timeout => "timeout",
        _ => "model-error"
    };

    private static ExtractorRun CreateExtractor(
        CloudArguments arguments,
        OllamaEndpoint endpoint,
        string model,
        OllamaRequestRecorder recorder,
        string repositoryRoot,
        IReceiptOcrRowReader? rowReader)
    {
        var isPhoto = arguments.Configuration == CloudPhoto;
        // Production defaults for every limit (num_ctx, num_predict, timeouts):
        // only the endpoint and the model tag differ from the local pipelines.
        var options = new ReceiptRecognitionOptions
        {
            Pipeline = isPhoto ? "ollama" : AppleGeometryOllamaExtractor.PipelineId,
            Mode = ReceiptRecognitionMode.Vision,
            BaseAddress = endpoint.BaseAddress.ToString(),
            VisionModel = isPhoto ? model : new ReceiptRecognitionOptions().VisionModel,
            RowsParserModel = isPhoto ? new ReceiptRecognitionOptions().RowsParserModel : model,
            VerifyModelAvailability = !arguments.SkipModelCheck,
            AppleSidecarPath = SidecarPath(repositoryRoot)
        };
        var descriptors = new[] { options.Pipeline }
            .Concat(options.FallbackPipelines)
            .Select(id => new ReceiptRecognitionPipelineDescriptor(id));
        var validation = new ReceiptRecognitionOptionsValidator(descriptors).Validate(null, options);
        if (validation.Failed)
        {
            throw new InvalidOperationException(
                $"Cloud benchmark options are invalid: {string.Join("; ", validation.Failures ?? [])}");
        }

        var timeout = TimeSpan.FromSeconds(isPhoto ? options.RequestTimeoutSeconds : options.RowsParserRequestTimeoutSeconds);
        var client = endpoint.CreateClient(timeout, recorder);
        var disposables = new List<IDisposable> { client };
        if (isPhoto)
        {
            var provider = new OllamaStructuredVisionProvider(client, Options.Create(options), NullLogger<OllamaStructuredVisionProvider>.Instance);
            disposables.Add(provider);
            var resolver = new StructuredVisionProviderResolver([provider]);
            return new ExtractorRun(
                new OllamaReceiptExtractor(new ReceiptAnalysisWorkflow(resolver), resolver, NullLogger<OllamaReceiptExtractor>.Instance),
                disposables);
        }

        var textProvider = new OllamaStructuredTextProvider(client, Options.Create(options), NullLogger<OllamaStructuredTextProvider>.Instance);
        disposables.Add(textProvider);
        var textResolver = new StructuredTextProviderResolver([textProvider]);
        return new ExtractorRun(
            new AppleGeometryOllamaExtractor(
                rowReader ?? throw new InvalidOperationException("apple-rows-cloud needs the shared OCR row reader."),
                new ReceiptRowsAnalysisWorkflow(textResolver),
                textResolver,
                NullLogger<AppleGeometryOllamaExtractor>.Instance),
            disposables);
    }

    private static async Task WriteResultAsync(string outputDirectory, CloudReceiptResult result)
    {
        var fileName = $"{result.Configuration}__{SafeFileName(result.Model)}__{result.ReceiptId}.json";
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, fileName), JsonSerializer.Serialize(result, JsonOptions));
    }

    private static string SafeFileName(string value) =>
        new(value.Select(character => char.IsLetterOrDigit(character) || character is '.' or '-' or '_' ? character : '_').ToArray());

    internal static ReceiptFile[] ResolveReceipts(string datasetDirectory, IReadOnlyList<string> requested)
    {
        var all = Directory.GetFiles(datasetDirectory, "receipt_*.jpg")
            .Order(StringComparer.Ordinal)
            .Select(path => new ReceiptFile(Path.GetFileNameWithoutExtension(path)["receipt_".Length..], path))
            .ToArray();
        if (requested.Count == 0)
        {
            return all;
        }

        var selected = requested
            .Select(id => all.FirstOrDefault(receipt => receipt.Id == id.PadLeft(2, '0'))
                ?? throw new ArgumentException($"Receipt '{id}' is not in {datasetDirectory}."))
            .ToArray();
        return selected;
    }

    internal static Dictionary<string, GroundTruth> LoadGroundTruth(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var result = new Dictionary<string, GroundTruth>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject().Where(property => !property.Name.StartsWith('_')))
        {
            var entry = property.Value;
            result[property.Name] = new GroundTruth(
                entry.GetProperty("merchant").GetString() ?? "",
                entry.GetProperty("date").GetString() ?? "",
                entry.GetProperty("total").GetDecimal(),
                entry.GetProperty("items").GetInt32());
        }

        return result;
    }

    private static string CreateMarkdown(CloudSummary summary, IReadOnlyDictionary<string, GroundTruth> groundTruth)
    {
        var builder = new StringBuilder();
        builder.AppendLine(CultureInfo.InvariantCulture, $"# Cloud benchmark: {summary.Configuration}");
        builder.AppendLine();
        builder.AppendLine(CultureInfo.InvariantCulture, $"Generated {summary.GeneratedAt:O} against `{summary.Endpoint}`.");
        if (summary.StopReason is not null)
        {
            builder.AppendLine();
            builder.AppendLine(CultureInfo.InvariantCulture, $"**Stopped early:** {summary.StopReason}.");
        }

        builder.AppendLine();
        builder.AppendLine("| Model | Receipts | Success | Total ±0.01 | Rate-limited | Unauthorized | Other failures | Mean time | Mean bytes sent |");
        builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var model in summary.Models)
        {
            var rows = summary.Results.Where(result => result.Model == model).ToArray();
            var successes = rows.Where(result => result.Status == "success").ToArray();
            builder.Append("| ").Append(model)
                .Append(" | ").Append(rows.Length)
                .Append(" | ").Append(successes.Length)
                .Append(" | ").Append(successes.Count(result => result.TotalMatches == true))
                .Append(" | ").Append(rows.Count(result => result.Status.EndsWith("rate-limited", StringComparison.Ordinal)))
                .Append(" | ").Append(rows.Count(result => result.Status.EndsWith("unauthorized", StringComparison.Ordinal)))
                .Append(" | ").Append(rows.Count(result => result.Status != "success"
                    && !result.Status.EndsWith("rate-limited", StringComparison.Ordinal)
                    && !result.Status.EndsWith("unauthorized", StringComparison.Ordinal)))
                .Append(" | ").Append(successes.Length == 0 ? "—" : $"{successes.Average(result => result.ElapsedMilliseconds) / 1000d:0.0}s")
                .Append(" | ").Append(successes.Length == 0 ? "—" : $"{successes.Average(result => result.RequestBytes):0}")
                .AppendLine(" |");
        }

        builder.AppendLine();
        builder.AppendLine("| Model | Receipt | Status | Time | Bytes sent | HTTP | Total (expected) | Items (expected) | Error |");
        builder.AppendLine("|---|---|---|---:|---:|---|---:|---:|---|");
        foreach (var result in summary.Results)
        {
            var expected = groundTruth.GetValueOrDefault(result.ReceiptId);
            builder.Append("| ").Append(result.Model)
                .Append(" | ").Append(result.ReceiptId)
                .Append(" | ").Append(result.Status)
                .Append(" | ").Append((result.ElapsedMilliseconds / 1000d).ToString("0.0", CultureInfo.InvariantCulture)).Append('s')
                .Append(" | ").Append(result.RequestBytes)
                .Append(" | ").Append(string.Join(",", result.Requests.Select(request => request.StatusCode?.ToString(CultureInfo.InvariantCulture) ?? "none")))
                .Append(" | ").Append(result.Parsed?.Total?.ToString("0.00", CultureInfo.InvariantCulture) ?? "—")
                .Append(" (").Append(expected?.Total.ToString("0.00", CultureInfo.InvariantCulture) ?? "?").Append(')')
                .Append(" | ").Append(result.Parsed?.Items.Count.ToString(CultureInfo.InvariantCulture) ?? "—")
                .Append(" (").Append(expected?.Items.ToString(CultureInfo.InvariantCulture) ?? "?").Append(')')
                .Append(" | ").Append(string.IsNullOrWhiteSpace(result.Error) ? "" : result.Error.Replace("|", "\\|", StringComparison.Ordinal).ReplaceLineEndings(" "))
                .AppendLine(" |");
        }

        builder.AppendLine();
        builder.AppendLine("Each receipt's JSON beside this file has a score.py-compatible `parsed` block, "
            + "the full extraction, and every HTTP request (path, status, bytes, time).");
        return builder.ToString();
    }

    internal sealed record ReceiptFile(string Id, string Path);

    /// <summary>
    /// Runs Apple OCR once per distinct photo and serves later reads from a JSON file
    /// in the run folder, so a rerun into the same --out folder also reuses the rows.
    /// </summary>
    private sealed class CachingRowReader(IReceiptOcrRowReader inner, string cacheDirectory) : IReceiptOcrRowReader
    {
        public string Id => inner.Id;

        public ModelInput? LastInput { get; private set; }

        public void ForgetLastInput() => LastInput = null;

        public async Task<ReceiptOcrPage> ReadAsync(ReceiptImageData image, CancellationToken cancellationToken)
        {
            var path = Path.Combine(cacheDirectory, Convert.ToHexStringLower(SHA256.HashData(image.Content.Span)) + ".json");
            byte[] serialized;
            if (File.Exists(path))
            {
                serialized = await File.ReadAllBytesAsync(path, cancellationToken);
            }
            else
            {
                var page = await inner.ReadAsync(image, cancellationToken);
                serialized = JsonSerializer.SerializeToUtf8Bytes(page, JsonOptions);
                Directory.CreateDirectory(cacheDirectory);
                await File.WriteAllBytesAsync(path, serialized, cancellationToken);
            }

            LastInput = ModelInput.Of("ocr-rows", serialized);
            // Always hand out the deserialized copy, including on the first read, so
            // the first model and the cached ones see byte-identical rows.
            return JsonSerializer.Deserialize<ReceiptOcrPage>(serialized, JsonOptions)
                ?? throw new InvalidOperationException($"OCR row cache {path} is empty.");
        }

        public Task<ReceiptRecognitionStatus> GetStatusAsync(CancellationToken cancellationToken) =>
            inner.GetStatusAsync(cancellationToken);
    }

    private sealed class ExtractorRun(IReceiptRecognitionPipeline extractor, List<IDisposable> disposables) : IDisposable
    {
        public IReceiptRecognitionPipeline Extractor { get; } = extractor;

        public void Dispose()
        {
            for (var index = disposables.Count - 1; index >= 0; index--)
            {
                disposables[index].Dispose();
            }
        }
    }
}

internal sealed record CloudArguments(
    string Configuration,
    IReadOnlyList<string> Models,
    IReadOnlyList<string> Receipts,
    string BaseUrl,
    string? OutputDirectory,
    bool SkipModelCheck,
    bool ContinueOnLimit)
{
    public const string Usage =
        "Usage: cloud --config cloud-photo|apple-rows-cloud --models m1,m2 [--receipts 01,05] "
        + "[--base-url https://ollama.com] [--out DIR] [--skip-model-check] [--continue-on-limit]";

    public static CloudArguments Parse(IReadOnlyList<string> args)
    {
        string? configuration = null;
        string[] models = [];
        string[] receipts = [];
        var baseUrl = CloudBenchmark.DefaultBaseUrl;
        string? output = null;
        var skipModelCheck = false;
        var continueOnLimit = false;
        for (var index = 0; index < args.Count; index++)
        {
            switch (args[index])
            {
                case "--config": configuration = Value(args, ref index); break;
                case "--models": models = List(Value(args, ref index)); break;
                case "--receipts": receipts = List(Value(args, ref index)); break;
                case "--base-url": baseUrl = Value(args, ref index); break;
                case "--out": output = Path.GetFullPath(Value(args, ref index)); break;
                case "--skip-model-check": skipModelCheck = true; break;
                case "--continue-on-limit": continueOnLimit = true; break;
                default: throw new ArgumentException($"Unknown argument '{args[index]}'.");
            }
        }

        if (configuration is not (CloudBenchmark.CloudPhoto or CloudBenchmark.AppleRowsCloud))
        {
            throw new ArgumentException($"--config must be {CloudBenchmark.CloudPhoto} or {CloudBenchmark.AppleRowsCloud}.");
        }

        if (models.Length == 0)
        {
            throw new ArgumentException("--models must name at least one model.");
        }

        return new(configuration, models, receipts, baseUrl, output, skipModelCheck, continueOnLimit);

        static string Value(IReadOnlyList<string> args, ref int index) =>
            ++index < args.Count ? args[index] : throw new ArgumentException($"{args[index - 1]} needs a value.");

        static string[] List(string value) =>
            value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}

internal sealed record ModelInput(string Kind, string Sha256, long Bytes)
{
    public static ModelInput Of(string kind, byte[] content) =>
        new(kind, Convert.ToHexStringLower(SHA256.HashData(content)), content.LongLength);
}

internal sealed record GroundTruth(string Merchant, string Date, decimal Total, int Items);

/// <summary>Shape read by test-data/receipts/household-2026-08/results/score.py.</summary>
internal sealed record ScorePyParsed(
    string? Merchant,
    string? Date,
    string? Currency,
    decimal? Total,
    IReadOnlyList<ScorePyItem> Items);

internal sealed record ScorePyItem(string Name, decimal? Quantity, decimal? UnitPrice, decimal? LineTotal);

internal sealed record CloudReceiptResult(
    string Configuration,
    string Model,
    string ReceiptId,
    string Status,
    string? Error,
    long ElapsedMilliseconds,
    double Seconds,
    long RequestBytes,
    IReadOnlyList<OllamaRequestRecord> Requests,
    bool? TotalMatches,
    ScorePyParsed? Parsed,
    ReceiptExtractionResult? Extraction)
{
    /// <summary>Hash and size of what the model received; null if OCR itself failed.</summary>
    public ModelInput? Input { get; init; }

    public static CloudReceiptResult Success(
        string configuration,
        string model,
        string receiptId,
        long elapsedMilliseconds,
        IReadOnlyList<OllamaRequestRecord> requests,
        ReceiptExtractionResult extraction,
        GroundTruth? expected)
    {
        var parsed = new ScorePyParsed(
            extraction.MerchantName,
            extraction.PurchasedAt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            extraction.Currency,
            extraction.ReceiptTotal,
            extraction.Items.Select(item => new ScorePyItem(item.Name, item.Quantity, item.UnitPrice, item.LineTotal)).ToArray());
        bool? totalMatches = expected is null || extraction.ReceiptTotal is null
            ? expected is null ? null : false
            : Math.Abs(extraction.ReceiptTotal.Value - expected.Total) <= 0.01m;
        return new(configuration, model, receiptId, "success", null, elapsedMilliseconds, elapsedMilliseconds / 1000d,
            requests.Sum(request => request.RequestBytes), requests, totalMatches, parsed, extraction);
    }

    public static CloudReceiptResult Failed(
        string configuration,
        string model,
        string receiptId,
        string status,
        string? error,
        long elapsedMilliseconds,
        IReadOnlyList<OllamaRequestRecord> requests) =>
        new(configuration, model, receiptId, status, error, elapsedMilliseconds, elapsedMilliseconds / 1000d,
            requests.Sum(request => request.RequestBytes), requests, null, null, null);
}

internal sealed record CloudSummary(
    DateTimeOffset GeneratedAt,
    string Configuration,
    string Endpoint,
    IReadOnlyList<string> Models,
    IReadOnlyList<string> Receipts,
    string? StopReason,
    IReadOnlyList<CloudReceiptResult> Results);
