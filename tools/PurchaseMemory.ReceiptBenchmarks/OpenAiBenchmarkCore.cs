using System.Globalization;
using PurchaseMemory.Application.Receipts;

// Network-free pieces of the `openai` benchmark command. Kept apart from
// OpenAiBenchmark.cs so the integration tests can compile this file directly.

/// <summary>
/// Arguments of the <c>openai</c> sub-command. The API key is never an argument:
/// the provider reads OPENAI_API_KEY from the environment itself.
/// </summary>
internal sealed record OpenAiArguments(
    IReadOnlyList<string> Models,
    IReadOnlyList<string> Receipts,
    string? OutputDirectory,
    string ImageDetail,
    string ReasoningEffort)
{
    public const string Usage =
        "Usage: openai --models m1,m2 [--receipts 01,05] [--out DIR] "
        + "[--detail low|high|original] [--reasoning minimal|low|medium|high]";

    public static OpenAiArguments Parse(IReadOnlyList<string> args)
    {
        string[] models = [];
        string[] receipts = [];
        string? output = null;
        // Production defaults of ReceiptRecognitionOptions.
        var detail = "high";
        var reasoning = "low";
        for (var index = 0; index < args.Count; index++)
        {
            switch (args[index])
            {
                case "--models": models = List(Value(args, ref index)); break;
                case "--receipts": receipts = List(Value(args, ref index)); break;
                case "--out": output = Path.GetFullPath(Value(args, ref index)); break;
                case "--detail": detail = Value(args, ref index).ToLowerInvariant(); break;
                case "--reasoning": reasoning = Value(args, ref index).ToLowerInvariant(); break;
                default: throw new ArgumentException($"Unknown argument '{args[index]}'.");
            }
        }

        if (models.Length == 0)
        {
            throw new ArgumentException("--models must name at least one model.");
        }

        if (detail is not ("low" or "high" or "original"))
        {
            throw new ArgumentException("--detail must be low, high or original.");
        }

        if (reasoning is not ("minimal" or "low" or "medium" or "high"))
        {
            throw new ArgumentException("--reasoning must be minimal, low, medium or high.");
        }

        return new(models, receipts, output, detail, reasoning);

        static string Value(IReadOnlyList<string> args, ref int index) =>
            ++index < args.Count ? args[index] : throw new ArgumentException($"{args[index - 1]} needs a value.");

        static string[] List(string value) =>
            value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}

/// <summary>Token usage of every provider call made for one receipt (repair retries included).</summary>
internal sealed record OpenAiUsage(int Calls, long InputTokens, long OutputTokens, IReadOnlyList<string> RequestIds)
{
    public decimal? CostUsd(string model) => OpenAiPrices.Cost(model, InputTokens, OutputTokens);
}

/// <summary>List prices, USD per 1M tokens, as given by the owner for this benchmark (24.09.2026).</summary>
internal static class OpenAiPrices
{
    private static readonly Dictionary<string, (decimal Input, decimal Output)> Prices = new(StringComparer.OrdinalIgnoreCase)
    {
        ["gpt-5.4-mini"] = (0.75m, 4.50m),
        ["gpt-6-luna"] = (0.10m, 0.50m),
        ["gpt-6-sol"] = (2m, 10m),
        ["gpt-6-astra"] = (10m, 50m)
    };

    public static decimal? Cost(string model, long inputTokens, long outputTokens) =>
        Prices.TryGetValue(model, out var price)
            ? decimal.Round((inputTokens * price.Input + outputTokens * price.Output) / 1_000_000m, 6)
            : null;

    public static string Describe(decimal? cost) =>
        cost is null ? "—" : "$" + cost.Value.ToString("0.0000", CultureInfo.InvariantCulture);
}

/// <summary>
/// Decorator that sums usage across all calls of one receipt: the workflow may
/// retry once with a repair prompt, and its metadata only reports the last call.
/// </summary>
internal sealed class UsageCountingVisionProvider(IStructuredVisionProvider inner) : IStructuredVisionProvider
{
    private readonly List<StructuredVisionResponse> _responses = [];
    private int _calls;

    public string Id => inner.Id;

    public async Task<StructuredVisionResponse> GenerateAsync(StructuredVisionRequest request, CancellationToken cancellationToken)
    {
        _calls++;
        var response = await inner.GenerateAsync(request, cancellationToken);
        _responses.Add(response);
        return response;
    }

    public Task<StructuredVisionProviderStatus> GetStatusAsync(CancellationToken cancellationToken) =>
        inner.GetStatusAsync(cancellationToken);

    /// <summary>
    /// Returns and resets the usage since the previous call. A call that failed
    /// before a response counts in <see cref="OpenAiUsage.Calls"/> without tokens.
    /// </summary>
    public OpenAiUsage TakeUsage()
    {
        var usage = new OpenAiUsage(
            _calls,
            _responses.Sum(response => response.InputTokens ?? 0),
            _responses.Sum(response => response.OutputTokens ?? 0),
            _responses.Select(response => response.ProviderRequestId).OfType<string>().ToArray());
        _responses.Clear();
        _calls = 0;
        return usage;
    }
}
