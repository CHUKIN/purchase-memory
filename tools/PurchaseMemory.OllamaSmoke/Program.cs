using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PurchaseMemory.Application.Receipts;
using PurchaseMemory.Infrastructure.Recognition;

var imagePath = args.Length > 0
    ? Path.GetFullPath(args[0])
    : Path.GetFullPath("test-data/receipts/public/lidl-2025-official-sample.png");
if (!File.Exists(imagePath))
{
    throw new FileNotFoundException("Receipt image was not found.", imagePath);
}

var mode = ParseMode(Environment.GetEnvironmentVariable("OLLAMA_MODE"));
var options = Options.Create(new ReceiptRecognitionOptions
{
    Mode = mode,
    BaseAddress = Environment.GetEnvironmentVariable("OLLAMA_BASE_ADDRESS")
        ?? "http://127.0.0.1:11434",
    OcrModel = Environment.GetEnvironmentVariable("OLLAMA_OCR_MODEL")
        ?? "glm-ocr:latest",
    ParserModel = Environment.GetEnvironmentVariable("OLLAMA_PARSER_MODEL")
        ?? "qwen3.5:4b",
    VisionModel = Environment.GetEnvironmentVariable("OLLAMA_VISION_MODEL")
        ?? "qwen3.5:9b",
});

using var httpClient = new HttpClient
{
    BaseAddress = new Uri(options.Value.BaseAddress.TrimEnd('/') + '/', UriKind.Absolute),
    Timeout = TimeSpan.FromSeconds(options.Value.RequestTimeoutSeconds)
};
using var provider = new OllamaStructuredVisionProvider(
    httpClient,
    options,
    NullLogger<OllamaStructuredVisionProvider>.Instance);
var status = await provider.GetStatusAsync(CancellationToken.None);
if (!status.Available)
{
    throw new InvalidOperationException(status.Message);
}

var capture = new CapturingProvider(provider);
var workflow = new ReceiptAnalysisWorkflow(
    new StructuredVisionProviderResolver([capture]));
ReceiptAnalysisResult result;
try
{
    result = await workflow.AnalyzeAsync(
        new ReceiptAnalysisRequest(
            Guid.NewGuid(),
            Guid.NewGuid().ToString("N"),
            [new ReceiptImageData(await File.ReadAllBytesAsync(imagePath), ContentTypeFor(imagePath))],
            provider.Id),
        CancellationToken.None);
}
catch (ReceiptStructuredOutputException) when (
    string.Equals(
        Environment.GetEnvironmentVariable("OLLAMA_SMOKE_INCLUDE_RAW"),
        "true",
        StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine(capture.LastRawJson);
    throw;
}

Console.WriteLine(JsonSerializer.Serialize(
    new
    {
        image = Path.GetFileName(imagePath),
        result.Extraction,
        result.Warnings,
        result.Metadata
    },
    new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));

static string ContentTypeFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
{
    ".jpg" or ".jpeg" => "image/jpeg",
    ".png" => "image/png",
    ".webp" => "image/webp",
    _ => throw new NotSupportedException($"Unsupported receipt image: {path}")
};

static ReceiptRecognitionMode ParseMode(string? value) =>
    Enum.TryParse<ReceiptRecognitionMode>(value, ignoreCase: true, out var mode)
        ? mode
        : ReceiptRecognitionMode.Hybrid;

sealed class CapturingProvider(IStructuredVisionProvider inner) : IStructuredVisionProvider
{
    public string Id => inner.Id;

    public string? LastRawJson { get; private set; }

    public async Task<StructuredVisionResponse> GenerateAsync(
        StructuredVisionRequest request,
        CancellationToken cancellationToken)
    {
        var response = await inner.GenerateAsync(request, cancellationToken);
        LastRawJson = response.RawJson;
        return response;
    }

    public Task<StructuredVisionProviderStatus> GetStatusAsync(CancellationToken cancellationToken) =>
        inner.GetStatusAsync(cancellationToken);
}
