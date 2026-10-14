using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PurchaseMemory.Application.Analytics;
using PurchaseMemory.Domain.Products;
using PurchaseMemory.Infrastructure.Analytics;
using PurchaseMemory.Infrastructure.Recognition;

// Opt-in, paid: runs a real analytics planner (OpenAI or Claude) over the fixed
// question set in test-data/analytics/planner_questions.json on a synthetic
// household (no database, never the owner's data) and scores each plan.
// Run: zsh -ic './scripts/dotnet.sh run --project tools/PurchaseMemory.AnalyticsPlannerBenchmark --
//        --provider anthropic|openai [--effort low|medium|high] [--budget-usd 0.05] [--out results.json]'
// Keys are read from ANTHROPIC_API_KEY / OPENAI_API_KEY by the planners themselves and never printed.

var provider = ArgValue("--provider") ?? "anthropic";
var effort = ArgValue("--effort") ?? "low";
var budgetUsd = decimal.Parse(ArgValue("--budget-usd") ?? "0.05", CultureInfo.InvariantCulture);
var questionsPath = ArgValue("--questions") ?? "test-data/analytics/planner_questions.json";
var outPath = ArgValue("--out");
var only = ArgValue("--only");

var root = JsonNode.Parse(File.ReadAllText(questionsPath))!.AsObject();
var today = DateOnly.Parse(root["today"]!.GetValue<string>(), CultureInfo.InvariantCulture);
var categories = ProductCategorySeed.Categories
    .Select(category => new AnalyticsCategory(category.Id, category.ParentId, category.Name, category.SystemKey))
    .ToList();
var keyById = categories.ToDictionary(category => category.Id, category => category.SystemKey!);
var idByKey = categories.ToDictionary(category => category.SystemKey!, category => category.Id);
var vocabularyNode = root["vocabulary"]!;
var tags = Strings(vocabularyNode["tags"]).Select(name => new AnalyticsTag(Guid.NewGuid(), name)).ToList();
var tagNameById = tags.ToDictionary(tag => tag.Id, tag => tag.Name);
var vocabulary = new AnalyticsVocabulary(categories, Strings(vocabularyNode["merchants"]), Strings(vocabularyNode["brands"]), tags);

var options = new AnalyticsOptions
{
    AnthropicEffort = Enum.Parse<AnthropicEffort>(effort, ignoreCase: true),
    OpenAiReasoningEffort = Enum.Parse<OpenAiReasoningEffort>(effort, ignoreCase: true)
};
IAnalyticsQueryPlanner planner = provider == OpenAiAnalyticsQueryPlanner.ProviderId
    ? new OpenAiAnalyticsQueryPlanner(Options.Create(options))
    : new AnthropicAnalyticsQueryPlanner(Options.Create(options), NullLogger<AnthropicAnalyticsQueryPlanner>.Instance);
if (!planner.IsConfigured)
{
    Console.Error.WriteLine($"The {provider} key is not set; nothing was called.");
    return 2;
}

// Both models list $0.10 / $0.50 per 1M input / output tokens (OpenAiBenchmarkCore, AnthropicPricing).
const decimal InputPrice = 0.10m, OutputPrice = 0.50m;
var results = new JsonArray();
decimal spent = 0;
int calls = 0, valid = 0, correct = 0;
var latencies = new List<long>();
foreach (var item in root["questions"]!.AsArray().Select(node => node!.AsObject()))
{
    var id = item["id"]!.GetValue<string>();
    if (only is not null && !only.Split(',').Contains(id))
    {
        continue;
    }

    if (spent >= budgetUsd)
    {
        Console.WriteLine($"Budget {budgetUsd} USD reached; stopping.");
        break;
    }

    var question = item["question"]!.GetValue<string>();
    var previous = item["previous"] is JsonObject previousNode
        ? new AnalyticsPlan
        {
            Metric = Enum.Parse<AnalyticsMetric>(previousNode["metric"]!.GetValue<string>(), ignoreCase: true),
            CategoryIds = Strings(previousNode["categoryKeys"]).Select(key => idByKey[key]).ToList(),
            From = DateOnly.Parse(previousNode["from"]!.GetValue<string>(), CultureInfo.InvariantCulture),
            To = DateOnly.Parse(previousNode["to"]!.GetValue<string>(), CultureInfo.InvariantCulture)
        }
        : null;

    calls++;
    var stopwatch = Stopwatch.StartNew();
    AnalyticsPlanningResult planning;
    try
    {
        planning = await planner.PlanAsync(new AnalyticsPlanningRequest(question, previous, today, vocabulary), CancellationToken.None);
    }
    catch (AnalyticsPlanningException exception)
    {
        Console.WriteLine($"- {id}: FAILED ({exception.Message}) in {stopwatch.ElapsedMilliseconds} ms");
        results.Add(new JsonObject { ["id"] = id, ["valid"] = false, ["error"] = exception.Message, ["ms"] = stopwatch.ElapsedMilliseconds });
        continue;
    }

    stopwatch.Stop();
    latencies.Add(stopwatch.ElapsedMilliseconds);
    valid++;
    var cost = ((planning.InputTokens ?? 0) * InputPrice + (planning.OutputTokens ?? 0) * OutputPrice) / 1_000_000m;
    spent += cost;
    var plan = AnalyticsPlanValidator.Validate(planning.Plan, vocabulary, new HashSet<Guid>(), today).Plan;
    // "anyOf" lists whole alternative expectations; the closest one is reported.
    var expected = item["expected"]!.AsObject();
    var failures = expected["anyOf"] is JsonArray alternatives
        ? alternatives.Select(alternative => Score(alternative!.AsObject(), planning.Understood, plan)).MinBy(list => list.Count)!
        : Score(expected, planning.Understood, plan);
    if (failures.Count == 0)
    {
        correct++;
    }

    var summary = Describe(planning.Understood, plan);
    Console.WriteLine($"- {id}: {(failures.Count == 0 ? "OK" : "WRONG " + string.Join("; ", failures))} | {summary} | {stopwatch.ElapsedMilliseconds} ms, tokens {planning.InputTokens}/{planning.OutputTokens}");
    results.Add(new JsonObject
    {
        ["id"] = id,
        ["valid"] = true,
        ["correct"] = failures.Count == 0,
        ["failures"] = new JsonArray(failures.Select(failure => (JsonNode?)failure).ToArray()),
        ["plan"] = summary,
        ["ms"] = stopwatch.ElapsedMilliseconds,
        ["inputTokens"] = planning.InputTokens,
        ["outputTokens"] = planning.OutputTokens,
        ["costUsd"] = cost
    });
}

var median = latencies.Count == 0 ? 0 : latencies.Order().ElementAt(latencies.Count / 2);
Console.WriteLine();
Console.WriteLine($"{provider} {planner.ModelName} effort {effort}: calls {calls}, valid {valid}, correct {correct}; median latency {median} ms; max {(latencies.Count == 0 ? 0 : latencies.Max())} ms");
Console.WriteLine($"Total cost USD {spent:0.000000}; per question {(valid == 0 ? 0 : spent / valid):0.000000}");
if (outPath is not null)
{
    File.WriteAllText(outPath, new JsonObject
    {
        ["provider"] = provider, ["model"] = planner.ModelName, ["effort"] = effort,
        ["calls"] = calls, ["valid"] = valid, ["correct"] = correct, ["medianMs"] = median, ["costUsd"] = spent,
        ["results"] = results
    }.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
}

return 0;

List<string> Score(JsonObject expected, bool understood, AnalyticsPlan plan)
{
    var failures = new List<string>();
    var expectUnderstood = expected["understood"]?.GetValue<bool>() ?? true;
    if (understood != expectUnderstood)
    {
        failures.Add($"understood {understood}");
    }

    if (!expectUnderstood)
    {
        return failures;
    }

    var metrics = Strings(expected["metric"]);
    var metric = JsonNamingPolicy.CamelCase.ConvertName(plan.Metric.ToString());
    if (!metrics.Contains(metric))
    {
        failures.Add($"metric {metric}");
    }

    var planKeys = plan.CategoryIds.Select(categoryId => keyById[categoryId]).ToList();
    if (expected["what"] is JsonValue all && all.GetValue<string>() == "all")
    {
        if (planKeys.Count > 0 || plan.NameTerms.Count > 0)
        {
            failures.Add("what should be all purchases");
        }
    }
    else if (expected["what"] is JsonObject what)
    {
        var keys = Strings(what["categoryKeys"]);
        var stems = Strings(what["terms"]);
        var termHit = plan.NameTerms.Any(term => stems.Any(stem =>
            term.Contains(stem, StringComparison.OrdinalIgnoreCase) || stem.Contains(term, StringComparison.OrdinalIgnoreCase)));
        if (planKeys.Any(key => !keys.Contains(key)) || (planKeys.Count == 0 && !termHit))
        {
            failures.Add("what");
        }
    }

    CompareSet("merchants", Strings(expected["merchants"]), plan.Merchants);
    CompareSet("brands", Strings(expected["brands"]), plan.Brands);
    var planTags = plan.TagIds.Select(tagId => tagNameById[tagId]).ToList();
    var tagOptions = expected["tags"] is JsonArray tagArray && tagArray.Count > 0 && tagArray[0] is JsonArray
        ? tagArray.Select(Strings).ToList()
        : [Strings(expected["tags"])];
    if (!tagOptions.Any(option => option.Order().SequenceEqual(planTags.Order())))
    {
        failures.Add($"tags [{string.Join(",", planTags)}]");
    }

    var groupBys = expected["groupBy"] is null ? ["none"] : Strings(expected["groupBy"]);
    var groupBy = JsonNamingPolicy.CamelCase.ConvertName(plan.GroupBy.ToString());
    if (!groupBys.Contains(groupBy))
    {
        failures.Add($"groupBy {groupBy}");
    }

    var discount = JsonNamingPolicy.CamelCase.ConvertName(plan.Discount.ToString());
    if (discount != (expected["discount"]?.GetValue<string>() ?? "any"))
    {
        failures.Add($"discount {discount}");
    }

    if (expected["limit"] is { } limit && plan.Limit != limit.GetValue<int>())
    {
        failures.Add($"limit {plan.Limit}");
    }

    if (expected["periods"] is JsonArray periods
        && !periods.Any(period => Date(period![0]) == plan.From && Date(period[1]) == plan.To))
    {
        failures.Add($"period {plan.From}..{plan.To}");
    }

    return failures;

    void CompareSet(string name, List<string> want, IReadOnlyList<string> got)
    {
        if (!want.Order().SequenceEqual(got.Order()))
        {
            failures.Add($"{name} [{string.Join(",", got)}]");
        }
    }
}

string Describe(bool understood, AnalyticsPlan plan) =>
    understood
        ? $"{plan.Metric} cat[{string.Join(",", plan.CategoryIds.Select(categoryId => keyById[categoryId]))}] terms[{string.Join(",", plan.NameTerms)}] " +
          $"brands[{string.Join(",", plan.Brands)}] shops[{string.Join(",", plan.Merchants)}] tags[{string.Join(",", plan.TagIds.Select(tagId => tagNameById[tagId]))}] " +
          $"{plan.From}..{plan.To} group {plan.GroupBy} discount {plan.Discount} limit {plan.Limit}"
        : "not understood";

static DateOnly? Date(JsonNode? node) =>
    node is null ? null : DateOnly.Parse(node.GetValue<string>(), CultureInfo.InvariantCulture);

static List<string> Strings(JsonNode? node) =>
    node is JsonArray array ? array.Select(item => item!.GetValue<string>()).ToList() : [];

string? ArgValue(string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}
