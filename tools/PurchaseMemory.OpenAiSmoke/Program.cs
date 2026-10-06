using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PurchaseMemory.Application.Receipts;
using PurchaseMemory.Infrastructure.Recognition;

var model = Environment.GetEnvironmentVariable("OPENAI_MODEL");
if (string.IsNullOrWhiteSpace(model))
{
    throw new InvalidOperationException("Set OPENAI_MODEL before running the smoke test.");
}

if (string.IsNullOrWhiteSpace(
    Environment.GetEnvironmentVariable(OpenAiStructuredVisionProvider.ApiKeyEnvironmentVariable)))
{
    throw new InvalidOperationException(
        $"Set {OpenAiStructuredVisionProvider.ApiKeyEnvironmentVariable} before running the smoke test.");
}

var imagePath = args.Length > 0
    ? Path.GetFullPath(args[0])
    : Path.GetFullPath("test-data/receipts/public/lidl-2025-official-sample.png");
if (!File.Exists(imagePath))
{
    throw new FileNotFoundException("Receipt image was not found.", imagePath);
}

var options = Options.Create(new ReceiptRecognitionOptions
{
    OpenAiModel = model,
    OpenAiImageDetail = ParseDetail(
        Environment.GetEnvironmentVariable("OPENAI_IMAGE_DETAIL")),
    OpenAiReasoningEffort = ParseReasoning(
        Environment.GetEnvironmentVariable("OPENAI_REASONING_EFFORT")),
});
using var provider = new OpenAiStructuredVisionProvider(
    options,
    NullLogger<OpenAiStructuredVisionProvider>.Instance);
var status = await provider.GetStatusAsync(CancellationToken.None);
if (!status.Available)
{
    throw new InvalidOperationException(status.Message);
}

var workflow = new ReceiptAnalysisWorkflow(
    new StructuredVisionProviderResolver([provider]));
var result = await workflow.AnalyzeAsync(
    new ReceiptAnalysisRequest(
        Guid.NewGuid(),
        Guid.NewGuid().ToString("N"),
        [new ReceiptImageData(await File.ReadAllBytesAsync(imagePath), ContentTypeFor(imagePath))],
        provider.Id),
    CancellationToken.None);

Console.WriteLine(JsonSerializer.Serialize(
    new
    {
        result.Extraction,
        result.Warnings,
        result.Metadata
    },
    new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));

static OpenAiImageDetail ParseDetail(string? value) =>
    Enum.TryParse<OpenAiImageDetail>(value, ignoreCase: true, out var detail)
        ? detail
        : OpenAiImageDetail.High;

static OpenAiReasoningEffort ParseReasoning(string? value) =>
    Enum.TryParse<OpenAiReasoningEffort>(value, ignoreCase: true, out var effort)
        ? effort
        : OpenAiReasoningEffort.Low;

static string ContentTypeFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
{
    ".jpg" or ".jpeg" => "image/jpeg",
    ".png" => "image/png",
    ".webp" => "image/webp",
    _ => throw new NotSupportedException($"Unsupported receipt image: {path}")
};
