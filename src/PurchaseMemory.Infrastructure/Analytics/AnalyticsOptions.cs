using PurchaseMemory.Infrastructure.Recognition;

namespace PurchaseMemory.Infrastructure.Analytics;

/// <summary>
/// Settings for natural-language analytics questions (<c>Analytics__*</c>).
/// The model only turns a question into a small query plan, so the cheapest
/// text model is enough: gpt-6-luna costs $0.10 / $0.50 per 1M input / output
/// tokens in <c>OpenAiBenchmarkCore</c>, 20× less than gpt-6-sol, and it is the
/// model the categorizer already runs with reasoning Low.
/// </summary>
public sealed class AnalyticsOptions
{
    public const string SectionName = "Analytics";

    /// <summary>Off switch for questions. Plans edited with chips and the MCP tool still run: they call no model.</summary>
    public bool Enabled { get; set; } = true;

    public string Model { get; set; } = "gpt-6-luna";

    public int RequestTimeoutSeconds { get; set; } = 30;

    /// <summary>A plan is ≈100 tokens; the rest is headroom for Low reasoning.</summary>
    public int MaximumOutputTokens { get; set; } = 2_000;

    public OpenAiReasoningEffort? ReasoningEffort { get; set; } = OpenAiReasoningEffort.Low;

    /// <summary>The household's calendar: «today», months and weeks are counted in it.</summary>
    public string TimeZone { get; set; } = "Europe/Warsaw";
}
