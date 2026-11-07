using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PurchaseMemory.Application.Products.PhotoNames;
using PurchaseMemory.FindByPhotoBenchmark;
using PurchaseMemory.Infrastructure.Images;
using PurchaseMemory.Infrastructure.PhotoSearch;

// Opt-in, paid: runs the real «Найти по фото» matcher over a folder of
// labelled product photos (read only, never copied) against a candidate list
// from a file, and reports top-1 / top-3 hit rate, mean time and cost per
// search. No database, no API host. Every photo is shrunk the way the app
// does it (long side 1024 px, JPEG) before it is sent.
// Run (the key is read from ANTHROPIC_API_KEY by the matcher and never printed):
//   zsh -ic './scripts/dotnet.sh run --project tools/PurchaseMemory.FindByPhotoBenchmark -- <photos-folder> <labels.csv> <candidates.json|folder> <out.csv> --budget-usd 0.10 [--model claude-haiku-5-5] [--limit 30] [--max-candidates 300]'
// labels.csv: header `file,expected`; expected = product id or exact name (Polish, Russian or receipt), `;` between
// several right answers, or `none` for a product that is not in the list.
// candidates: a JSON array (productId, receiptName, namePl, nameRu, brand, category) or saved
// GET /api/products pages ({"items":[...]}); a folder means every *.json in it.

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
if (!Directory.Exists(folder) || !File.Exists(arguments.LabelsPath)
    || !(File.Exists(arguments.CandidatesPath) || Directory.Exists(arguments.CandidatesPath)))
{
    Console.Error.WriteLine("The photos folder, the labels file and the candidates file or folder must exist.");
    return 2;
}

var candidateFiles = Directory.Exists(arguments.CandidatesPath)
    ? Directory.EnumerateFiles(arguments.CandidatesPath, "*.json").Order(StringComparer.Ordinal).ToList()
    : [arguments.CandidatesPath];
List<PurchaseMemory.Application.Products.PhotoSearch.ProductPhotoCandidate> candidates;
try
{
    candidates = BenchmarkInputs.ParseCandidates(candidateFiles.Select(path => File.ReadAllText(path, Encoding.UTF8)))
        .Take(arguments.MaxCandidates)
        .ToList();
}
catch (JsonException exception)
{
    Console.Error.WriteLine($"Candidates: {exception.Message}");
    return 2;
}

var labels = BenchmarkInputs.ParseLabels(await File.ReadAllTextAsync(arguments.LabelsPath, Encoding.UTF8));
var compressor = new ReceiptImageCompressor(new ReceiptDisplayCopyOptions { MaximumLongSidePixels = 1024, JpegQuality = 70 });
var photos = new List<BenchmarkPhoto>();
foreach (var label in labels)
{
    var path = Path.Combine(folder, label.File);
    var expected = BenchmarkInputs.Resolve(label, candidates);
    if (!File.Exists(path) || expected is null)
    {
        Console.WriteLine($"- {label.File}: {(File.Exists(path) ? "the expected product is not in the candidate list" : "file not found")}, skipped (no call)");
        continue;
    }

    photos.Add(new BenchmarkPhoto(label, expected, () =>
    {
        var copy = compressor.TryCreate(File.ReadAllBytes(path));
        return copy is null ? null : ProductPhotoRules.Check(copy.Content);
    }));
}

photos = photos.Take(arguments.Limit).ToList();
var options = new ProductPhotoSearchOptions { Enabled = true, MaximumCandidates = arguments.MaxCandidates };
if (arguments.Model is { } model)
{
    options.Model = model;
}

using var matcher = new AnthropicProductPhotoMatcher(Options.Create(options), NullLogger<AnthropicProductPhotoMatcher>.Instance);
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

Console.WriteLine($"{photos.Count} photos, {candidates.Count} candidates, model {options.Model}, budget {arguments.BudgetUsd} USD");
var report = await new FindByPhotoBenchmarkRunner(matcher, arguments.BudgetUsd, Console.WriteLine)
    .RunAsync(photos, candidates, CancellationToken.None);

var output = Path.GetFullPath(arguments.OutputPath);
await File.WriteAllLinesAsync(output, BenchmarkOutput.CsvRows(report), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
var summary = BenchmarkOutput.SummaryText(report.Summary);
await File.WriteAllTextAsync(Path.ChangeExtension(output, ".summary.txt"), summary + Environment.NewLine, Encoding.UTF8);
Console.WriteLine(summary);
Console.WriteLine($"Wrote {report.Outcomes.Count} rows to {output}");
return 0;
