using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PurchaseMemory.Application.Products.PhotoNames;
using PurchaseMemory.Infrastructure.Images;
using PurchaseMemory.Infrastructure.PhotoNames;

// Opt-in, paid: runs the real «Название по фото» namer over a folder of product
// photos (read only, never copied) and writes one CSV row per photo. No
// database, no API host. Every photo is shrunk the way the app does it
// (long side 1024 px, JPEG) before it is sent.
// Run (the key is read from ANTHROPIC_API_KEY by the namer and never printed):
//   zsh -ic './scripts/dotnet.sh run --project tools/PurchaseMemory.PhotoNameBenchmark -- <photos-folder> <out.csv> --budget-usd 0.05 [--model claude-haiku-5-5] [--limit 20] [--hints hints.csv]'
// --hints: optional CSV (UTF-8, header row `file,receipt_name,brand`) giving each photo the receipt
// text and brand the app would pass as the hint; a photo with no row is sent without a hint.

if (args.Length < 2 || args[0].StartsWith("--", StringComparison.Ordinal) || args[1].StartsWith("--", StringComparison.Ordinal))
{
    Console.Error.WriteLine("Usage: <photos-folder> <out.csv> --budget-usd <amount> [--model <id>] [--limit <n>] [--hints <csv>]");
    return 2;
}

var folder = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
if (ArgValue("--budget-usd") is not { } budgetText
    || !decimal.TryParse(budgetText, NumberStyles.Number, CultureInfo.InvariantCulture, out var budgetUsd)
    || budgetUsd <= 0)
{
    Console.Error.WriteLine("--budget-usd is required and must be a positive amount, e.g. --budget-usd 0.05");
    return 2;
}

var limit = int.TryParse(ArgValue("--limit"), CultureInfo.InvariantCulture, out var parsedLimit) && parsedLimit > 0 ? parsedLimit : int.MaxValue;
if (!Directory.Exists(folder))
{
    Console.Error.WriteLine($"Folder not found: {folder}");
    return 2;
}

var options = new ProductPhotoNamingOptions();
if (ArgValue("--model") is { Length: > 0 } model)
{
    options.Model = model;
}

using var namer = new AnthropicProductPhotoNamer(Options.Create(options), NullLogger<AnthropicProductPhotoNamer>.Instance);
if (!namer.IsConfigured)
{
    Console.Error.WriteLine("ANTHROPIC_API_KEY is not set; nothing was called.");
    return 2;
}

var hints = new Dictionary<string, ProductPhotoHint>(StringComparer.OrdinalIgnoreCase);
if (ArgValue("--hints") is { Length: > 0 } hintsPath)
{
    if (!File.Exists(hintsPath))
    {
        Console.Error.WriteLine($"Hints file not found: {hintsPath}");
        return 2;
    }

    foreach (var fields in ParseCsv(await File.ReadAllTextAsync(hintsPath, Encoding.UTF8)).Skip(1))
    {
        if (fields.Count >= 2 && fields[0].Trim().Length > 0 && fields[1].Trim().Length > 0)
        {
            var brandField = fields.Count > 2 ? fields[2].Trim() : "";
            hints[fields[0].Trim()] = new ProductPhotoHint(fields[1].Trim(), brandField.Length > 0 ? brandField : null);
        }
    }

    Console.WriteLine($"{hints.Count} hints loaded");
}

string[] extensions = [".jpg", ".jpeg", ".png", ".webp", ".heic"];
var photos = Directory.EnumerateFiles(folder)
    .Where(path => extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
    .Order(StringComparer.Ordinal)
    .Take(limit)
    .ToList();
Console.WriteLine($"{photos.Count} photos, model {options.Model}, budget {budgetUsd.ToString(CultureInfo.InvariantCulture)} USD");

// Same shrink as the app (compressImage.ts, photoForNaming): 1024 px, quality ~70.
var compressor = new ReceiptImageCompressor(new ReceiptDisplayCopyOptions { MaximumLongSidePixels = 1024, JpegQuality = 70 });
// Before the first answer the next call is assumed to cost this much; after it,
// the most expensive call so far. The run stops before a call could cross the budget.
var assumedCallCost = 0.002m;
var maxObservedCost = 0m;
var spent = 0m;
var rows = new List<string> { "file,hint,name_pl,name_ru,brand,ms,input_tokens,output_tokens,cost_usd,image_bytes,error" };

foreach (var path in photos)
{
    if (spent + assumedCallCost > budgetUsd)
    {
        Console.WriteLine($"Budget: {spent.ToString(CultureInfo.InvariantCulture)} spent, next call could cross {budgetUsd.ToString(CultureInfo.InvariantCulture)} USD; stopping.");
        break;
    }

    var name = Path.GetFileName(path);
    hints.TryGetValue(name, out var hint);
    var hintText = hint is null ? null : hint.Brand is null ? hint.ReceiptText : $"{hint.ReceiptText} [{hint.Brand}]";
    var copy = compressor.TryCreate(await File.ReadAllBytesAsync(path));
    if (copy is null)
    {
        rows.Add(Csv(name, hintText, null, null, null, 0, null, null, null, 0, "could not decode"));
        Console.WriteLine($"- {name}: could not decode, skipped (no call)");
        continue;
    }

    var stopwatch = Stopwatch.StartNew();
    try
    {
        var image = ProductPhotoRules.Check(copy.Content);
        var reading = await namer.ReadAsync(image, hint, CancellationToken.None);
        stopwatch.Stop();
        // No price known for this model: count the call at the assumed cost so the cap still holds.
        var cost = reading.CostUsd ?? assumedCallCost;
        spent += cost;
        maxObservedCost = Math.Max(maxObservedCost, cost);
        assumedCallCost = maxObservedCost > 0 ? maxObservedCost : assumedCallCost;
        rows.Add(Csv(name, hintText, reading.NamePl, reading.NameRu, reading.Brand, stopwatch.ElapsedMilliseconds,
            reading.InputTokens, reading.OutputTokens, reading.CostUsd, copy.Content.Length, null));
        Console.WriteLine($"- {name}: {reading.NamePl ?? "—"} | {reading.NameRu ?? "—"} ({stopwatch.ElapsedMilliseconds} ms, ${cost.ToString("0.000000", CultureInfo.InvariantCulture)})");
    }
    catch (Exception exception) when (exception is ProductPhotoNamingUnavailableException or InvalidProductPhotoException)
    {
        stopwatch.Stop();
        // A failed call reports no usage but may still be billed: count it as a full call.
        spent += assumedCallCost;
        rows.Add(Csv(name, hintText, null, null, null, stopwatch.ElapsedMilliseconds, null, null, null, copy.Content.Length, exception.Message));
        Console.WriteLine($"- {name}: FAILED ({exception.Message})");
    }
}

await File.WriteAllLinesAsync(output, rows, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
Console.WriteLine($"Wrote {rows.Count - 1} rows to {output}; total cost USD {spent.ToString("0.000000", CultureInfo.InvariantCulture)}");
return 0;

string? ArgValue(string flag)
{
    var index = Array.IndexOf(args, flag);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

static string Csv(string file, string? hint, string? namePl, string? nameRu, string? brand, long ms, long? input, long? outputTokens, decimal? cost, int imageBytes, string? error) =>
    string.Join(',',
        Quote(file),
        Quote(hint),
        Quote(namePl),
        Quote(nameRu),
        Quote(brand),
        ms.ToString(CultureInfo.InvariantCulture),
        input?.ToString(CultureInfo.InvariantCulture) ?? "",
        outputTokens?.ToString(CultureInfo.InvariantCulture) ?? "",
        cost?.ToString("0.000000", CultureInfo.InvariantCulture) ?? "",
        imageBytes.ToString(CultureInfo.InvariantCulture),
        Quote(error));

static string Quote(string? value) =>
    value is null ? "" : "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

// Minimal CSV reader: quoted fields, "" as an escaped quote, commas and line breaks inside quotes.
static List<List<string>> ParseCsv(string text)
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
        else field.Append(c);
    }

    if (field.Length > 0 || fields.Count > 0)
    {
        fields.Add(field.ToString());
        records.Add(fields);
    }

    return records;
}
