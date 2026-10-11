using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PurchaseMemory.Application.Products.Tags;
using PurchaseMemory.Infrastructure.Tagging;

// Opt-in, paid: runs the real Claude tag suggester on a synthetic household
// (no database at all, never the owner's) and prints picks, tokens and cost.
// Run: zsh -ic './scripts/dotnet.sh run --project tools/PurchaseMemory.TagSuggestionBenchmark -- [--locale en] [--budget-usd 0.05]'
// The key is read from ANTHROPIC_API_KEY by the suggester itself and never printed.

var locale = ArgValue("--locale") ?? "ru";
var budgetUsd = decimal.Parse(ArgValue("--budget-usd") ?? "0.05", CultureInfo.InvariantCulture);

string[] tagNames =
[
    "для ребёнка", "веган", "без глютена", "к празднику", "акция", "бытовая химия",
    "для кота", "завтрак", "здоровое питание", "алкоголь", "сладкое", "заморозка"
];
var tags = tagNames.Select(name => new TagSuggestionOption(Guid.NewGuid(), name)).ToList();
var byName = tags.ToDictionary(tag => tag.Name, tag => tag.TagId);

// Expected = tags a careful person would add; Acceptable = also fine if suggested.
var cases = new BenchmarkCase[]
{
    new(new("Kaszka mleczno-ryżowa malinowa", "BoboVita", "Детское питание", ["KASZKA ML-RYZ MALINA BOBOVITA"], ["biedronka"]), ["для ребёнка"], ["завтрак"]),
    new(new("Napój owsiany", "Alpro", "Напитки / Растительное молоко", ["NAPOJ OWSIANY ALPRO 1L"], ["lidl"]), ["веган"], ["здоровое питание", "завтрак"]),
    new(new("Tofu naturalne", null, "Растительные продукты", ["TOFU NATURALNE 180G"], ["lidl"]), ["веган"], ["здоровое питание"]),
    new(new("Piwo Tyskie Gronie", "Tyskie", "Алкоголь / Пиво", ["PIWO TYSKIE 0,5L PUSZ"], ["zabka"]), ["алкоголь"], []),
    new(new("Czekolada mleczna", "Milka", "Сладости", ["CZEKOLADA MILKA 100G"], ["biedronka"]), ["сладкое"], []),
    new(new("Płyn do naczyń cytrynowy", "Ludwik", "Бытовая химия", ["PLYN NACZ LUDWIK 900ML"], ["rossmann"]), ["бытовая химия"], []),
    new(new("Karma dla kota z kurczakiem", "Whiskas", "Для животных", ["WHISKAS KURCZAK 85G"], ["biedronka"]), ["для кота"], []),
    new(new("Pierogi ruskie mrożone", null, "Замороженные продукты", ["PIEROGI RUSKIE MROZ 450G"], ["lidl"]), ["заморозка"], []),
    new(new("Chleb bezglutenowy", "Schär", "Хлеб", ["CHLEB BEZGLUT SCHAR"], ["carrefour"]), ["без глютена"], ["здоровое питание"]),
    new(new("Płatki owsiane górskie", null, "Крупы и хлопья", ["PLATKI OWSIANE GORSKIE 500G"], ["biedronka"]), ["завтрак"], ["здоровое питание", "веган"]),
    new(new("Prosecco Spumante", null, "Алкоголь / Вино", ["WINO PROSECCO SPUM 0,75"], ["lidl"]), ["алкоголь"], ["к празднику"]),
    new(new("Mleko 3,2%", "Łaciate", "Молочное и яйца / Молоко", ["MLEKO LACIATE 3,2% 1L"], ["biedronka"]), [], ["завтрак"]),
    new(new("Jabłka Champion", null, "Фрукты", ["JABLKA CHAMPION LUZ"], ["biedronka"]), [], ["здоровое питание"]),
    new(new("Lody Ekipa", "Ekipa", "Замороженные / Мороженое", ["LODY EKIPA 500ML"], ["zabka"]), ["сладкое", "заморозка"], []),
    new(new("Pieluchy Premium Care 4", "Pampers", "Гигиена", ["PAMPERS PREM CARE 4 PROMOCJA"], ["rossmann"]), ["для ребёнка"], ["акция"])
};

var usage = new UsageCollector();
using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(usage).SetMinimumLevel(LogLevel.Information));
using var suggester = new AnthropicTagSuggester(Options.Create(new TagSuggestionOptions()), loggerFactory.CreateLogger<AnthropicTagSuggester>());
if (!suggester.IsConfigured)
{
    Console.Error.WriteLine("ANTHROPIC_API_KEY is not set; nothing was called.");
    return 2;
}

var language = locale.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? TagSuggestionLanguage.English : TagSuggestionLanguage.Russian;
int picked = 0, correct = 0, expectedTotal = 0, expectedFound = 0, failures = 0;
foreach (var item in cases)
{
    if (usage.TotalCostUsd >= budgetUsd)
    {
        Console.WriteLine($"Budget {budgetUsd} USD reached; stopping.");
        break;
    }

    IReadOnlyList<SuggestedTag> result;
    try
    {
        result = await suggester.SuggestAsync(item.Product, tags, language, CancellationToken.None);
    }
    catch (TagSuggestionsUnavailableException exception)
    {
        failures++;
        Console.WriteLine($"- {item.Product.Name}: FAILED ({exception.Message})");
        continue;
    }

    var names = result.Select(suggestion => tags.First(tag => tag.TagId == suggestion.TagId).Name).ToList();
    var good = names.Count(name => item.Expected.Contains(name) || item.Acceptable.Contains(name));
    picked += names.Count;
    correct += good;
    expectedTotal += item.Expected.Length;
    expectedFound += item.Expected.Count(names.Contains);
    var call = usage.LastCall;
    Console.WriteLine($"- {item.Product.Name}: [{string.Join(", ", result.Select(s => $"{tags.First(t => t.TagId == s.TagId).Name} — {s.Reason}"))}] " +
                      $"expected [{string.Join(", ", item.Expected)}] ok {good}/{names.Count}; {call}");
}

Console.WriteLine();
Console.WriteLine($"Calls: {usage.Calls}, failures: {failures}");
Console.WriteLine($"Precision (picked tags that are expected or acceptable): {correct}/{picked}");
Console.WriteLine($"Recall (expected tags that were picked): {expectedFound}/{expectedTotal}");
Console.WriteLine($"Tokens in/out: {usage.InputTokens}/{usage.OutputTokens}; total cost USD {usage.TotalCostUsd:0.000000}; per call {(usage.Calls == 0 ? 0 : usage.TotalCostUsd / usage.Calls):0.000000}; median latency ms {usage.MedianLatencyMs}");
return 0;

string? ArgValue(string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

internal sealed record BenchmarkCase(TagSuggestionProduct Product, string[] Expected, string[] Acceptable);

/// <summary>Reads tokens, cost and latency from the suggester's structured completion log (event 3411).</summary>
internal sealed class UsageCollector : ILoggerProvider, ILogger
{
    private readonly List<long> _latencies = [];

    public int Calls { get; private set; }
    public long InputTokens { get; private set; }
    public long OutputTokens { get; private set; }
    public decimal TotalCostUsd { get; private set; }
    public string LastCall { get; private set; } = "";
    public long MedianLatencyMs => _latencies.Count == 0 ? 0 : _latencies.Order().ElementAt(_latencies.Count / 2);

    public ILogger CreateLogger(string categoryName) => this;
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Dispose() { }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (eventId.Id == 3412)
        {
            Console.WriteLine("  " + formatter(state, exception));
        }

        if (eventId.Id != 3411 || state is not IReadOnlyList<KeyValuePair<string, object?>> values)
        {
            return;
        }

        object? Get(string key) => values.FirstOrDefault(pair => pair.Key == key).Value;
        var input = Convert.ToInt64(Get("InputTokens") ?? 0L, CultureInfo.InvariantCulture);
        var output = Convert.ToInt64(Get("OutputTokens") ?? 0L, CultureInfo.InvariantCulture);
        var cost = Get("CostUsd") is decimal value ? value : 0m;
        var latency = Convert.ToInt64(Get("DurationMs") ?? 0L, CultureInfo.InvariantCulture);
        Calls++;
        InputTokens += input;
        OutputTokens += output;
        TotalCostUsd += cost;
        _latencies.Add(latency);
        LastCall = $"{input}/{output} tok, ${cost:0.000000}, {latency} ms";
    }
}
