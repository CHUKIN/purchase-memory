using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PurchaseMemory.Application.Purchases.HaulPhoto;
using PurchaseMemory.HaulPhotoBenchmark;
using PurchaseMemory.Infrastructure.HaulPhoto;
using PurchaseMemory.Infrastructure.Recognition;

// Opt-in, paid: runs the real «Сфотографировать покупки» matcher over a folder
// of labelled haul photos (read only, never copied) and reports correct and
// wrong pairs, missed items, time and cost per photo. No database, no API
// host. Every photo is prepared exactly as the server does it (EXIF rotation,
// ≤ 1.15 MP copy for the model).
// Run (the key is read from ANTHROPIC_API_KEY by the matcher and never printed):
//   zsh -ic './scripts/dotnet.sh run --project tools/PurchaseMemory.HaulPhotoBenchmark -- <photos-folder> <hauls.json> <out.csv> --budget-usd 0.50 [--model claude-sonnet-5-5] [--effort medium] [--limit 10] [--crops <folder>]'
// hauls.json: [{"file": "IMG_1.jpg", "purchase": "purchase-1.json" | "lines": [{"name", "product", "quantity", "unit"}],
//               "expected": ["receipt name of every visible item", ...]}]

BenchmarkArguments arguments;
try
{
    arguments = BenchmarkArguments.Parse(args);
}
catch (ArgumentException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 2;
}

var folder = Path.GetFullPath(arguments.PhotosFolder);
if (!Directory.Exists(folder) || !File.Exists(arguments.LabelsPath))
{
    Console.Error.WriteLine("The photos folder and the labels file must exist.");
    return 2;
}

var labelsFolder = Path.GetDirectoryName(Path.GetFullPath(arguments.LabelsPath))!;
IReadOnlyList<HaulLabel> labels;
try
{
    labels = BenchmarkInputs.ParseLabels(
        await File.ReadAllTextAsync(arguments.LabelsPath, Encoding.UTF8),
        name => File.ReadAllText(Path.Combine(labelsFolder, name), Encoding.UTF8));
}
catch (Exception exception) when (exception is JsonException or IOException)
{
    Console.Error.WriteLine($"Labels: {exception.Message}");
    return 2;
}

var processor = new HaulPhotoImageProcessor();
var prepared = new Dictionary<string, HaulPhotoImages>(StringComparer.Ordinal);
var photos = new List<BenchmarkPhoto>();
foreach (var label in labels)
{
    var path = Path.Combine(folder, label.File);
    var expected = BenchmarkInputs.Resolve(label);
    if (!File.Exists(path) || expected is null || label.Lines.Count == 0)
    {
        var reason = !File.Exists(path) ? "file not found"
            : label.Lines.Count == 0 ? "no product lines"
            : "an expected name is not one of the lines";
        Console.WriteLine($"- {label.File}: {reason}, skipped (no call)");
        continue;
    }

    photos.Add(new BenchmarkPhoto(label, expected, () =>
    {
        var images = processor.Prepare(File.ReadAllBytes(path));
        if (images is null)
        {
            return null;
        }

        prepared[label.File] = images;
        return images.Model;
    }));
}

photos = photos.Take(arguments.Limit).ToList();
var options = new HaulPhotoOptions { Enabled = true };
if (arguments.Model is { } model)
{
    options.Model = model;
}

if (arguments.Effort is { } effort)
{
    options.Effort = Enum.Parse<AnthropicEffort>(effort, ignoreCase: true);
}

using var matcher = new AnthropicHaulPhotoMatcher(Options.Create(options), NullLogger<AnthropicHaulPhotoMatcher>.Instance);
if (!matcher.HasKnownPrice)
{
    // Without a price the spend cannot be tracked, so the budget could not hold.
    Console.Error.WriteLine($"No known price for model {options.Model}; add it to AnthropicPricing first. Nothing was called.");
    return 2;
}

if (!matcher.IsConfigured)
{
    Console.Error.WriteLine("ANTHROPIC_API_KEY is not set; nothing was called.");
    return 2;
}

string? cropsFolder = null;
if (arguments.CropsFolder is { } crops)
{
    cropsFolder = Path.GetFullPath(crops);
    Directory.CreateDirectory(cropsFolder);
}

void WriteCrops(BenchmarkPhoto photo, HaulPhotoImage image, HaulPhotoReading reading)
{
    if (cropsFolder is null || !prepared.TryGetValue(photo.Label.File, out var images))
    {
        return;
    }

    var codes = photo.Label.Lines.Select((line, index) => (line.LineId, Code: "l" + (index + 1).ToString(CultureInfo.InvariantCulture)))
        .ToDictionary(pair => pair.LineId, pair => pair.Code);
    var stem = Path.GetFileNameWithoutExtension(photo.Label.File);
    using var cropper = processor.OpenForCropping(images.Source);
    for (var index = 0; index < reading.Items.Count && cropper is not null; index++)
    {
        var item = reading.Items[index];
        var crop = cropper.Crop(item.Box);
        if (crop is null)
        {
            continue;
        }

        var line = item.LineId is { } id && codes.TryGetValue(id, out var code) ? code : "none";
        File.WriteAllBytes(Path.Combine(cropsFolder, $"{stem}-{index + 1:00}-{line}-{item.Confidence.ToString().ToLowerInvariant()}.jpg"), crop);
    }
}

var worstCase = matcher.EstimateCostUsd(HaulPhotoBenchmarkRunner.ConservativeInputTokens, options.MaximumOutputTokens)!.Value;
Console.WriteLine($"{photos.Count} photos, model {options.Model} (effort {options.Effort}), budget {arguments.BudgetUsd} USD, worst case per call {worstCase} USD");
var report = await new HaulPhotoBenchmarkRunner(matcher, arguments.BudgetUsd, worstCase, Console.WriteLine, WriteCrops)
    .RunAsync(photos, CancellationToken.None);

var output = Path.GetFullPath(arguments.OutputPath);
await File.WriteAllLinesAsync(output, BenchmarkOutput.CsvRows(report), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
var summary = BenchmarkOutput.SummaryText(report.Summary);
await File.WriteAllTextAsync(Path.ChangeExtension(output, ".summary.txt"), summary + Environment.NewLine, Encoding.UTF8);
Console.WriteLine(summary);
Console.WriteLine($"Wrote {report.Outcomes.Count} rows to {output}");
return 0;
