using Microsoft.Extensions.Options;
using PurchaseMemory.Infrastructure.Recognition;

namespace PurchaseMemory.Infrastructure.Analytics;

/// <summary>
/// Settings for natural-language analytics questions (<c>Analytics__*</c>).
/// The model only turns a question into a small query plan, so the cheapest
/// text models are enough: gpt-6-luna (reasoning Low, <c>OPENAI_API_KEY</c>)
/// or Claude Haiku 5.5 (effort Low, <c>ANTHROPIC_API_KEY</c>), chosen by
/// <see cref="Provider"/> with optional <see cref="FallbackProviders"/>; the
/// comparison is in <c>РЕЗУЛЬТАТЫ_АНАЛИТИКА_HAIKU.md</c>.
/// </summary>
public sealed class AnalyticsOptions
{
    public const string SectionName = "Analytics";

    /// <summary>Off switch for questions. Plans edited with chips and the MCP tool still run: they call no model.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The planner asked first: <c>anthropic</c> or <c>openai</c>.</summary>
    public string Provider { get; set; } = OpenAiAnalyticsQueryPlanner.ProviderId;

    /// <summary>
    /// Planners tried, in order, when the first one is not configured (no key) or
    /// fails (network, timeout, 429/5xx, refusal, cut-off or malformed answer).
    /// Configuration only (appsettings.json); empty means no fallback.
    /// </summary>
    public List<string> FallbackProviders { get; set; } = [];

    /// <summary>
    /// Per attempt; the person waits on the analytics screen. With a fallback
    /// configured every planner makes a single attempt (no SDK retries), so the
    /// worst case is one timeout per planner in the chain.
    /// </summary>
    public int RequestTimeoutSeconds { get; set; } = 30;

    public string AnthropicModel { get; set; } = "claude-haiku-5-5";

    public AnthropicEffort AnthropicEffort { get; set; } = AnthropicEffort.Low;

    /// <summary>A plan is ≈150 tokens; the rest is headroom for adaptive thinking.</summary>
    public int AnthropicMaximumOutputTokens { get; set; } = 4_000;

    /// <summary>Used only without a fallback; with one, the fallback is the retry.</summary>
    public int AnthropicMaximumRetries { get; set; } = 1;

    /// <summary>gpt-6-luna costs $0.10 / $0.50 per 1M input / output tokens.</summary>
    public string OpenAiModel { get; set; } = "gpt-6-luna";

    /// <summary>A plan is ≈100 tokens; the rest is headroom for Low reasoning.</summary>
    public int OpenAiMaximumOutputTokens { get; set; } = 2_000;

    public OpenAiReasoningEffort? OpenAiReasoningEffort { get; set; } = Recognition.OpenAiReasoningEffort.Low;

    /// <summary>The household's calendar: «today», months and weeks are counted in it.</summary>
    public string TimeZone { get; set; } = "Europe/Warsaw";

    internal static readonly string[] KnownProviders =
        [AnthropicAnalyticsQueryPlanner.ProviderId, OpenAiAnalyticsQueryPlanner.ProviderId];

    /// <summary>The primary provider, then the fallbacks: trimmed and lower-case.</summary>
    public IReadOnlyList<string> PlannerOrder() =>
        FallbackProviders.Prepend(Provider).Select(provider => Normalize(provider) ?? string.Empty).ToList();

    /// <summary>Whether another planner stands behind the first one.</summary>
    public bool HasFallback => FallbackProviders.Count > 0;

    internal static string? Normalize(string? provider) => provider?.Trim().ToLowerInvariant();
}

/// <summary>
/// Rejects bad <c>Analytics:*</c> values at startup (registered with
/// ValidateOnStart) instead of letting them surface on the first question.
/// </summary>
public sealed class AnalyticsOptionsValidator : IValidateOptions<AnalyticsOptions>
{
    public ValidateOptionsResult Validate(string? name, AnalyticsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var errors = new List<string>();
        if (!IsKnown(options.Provider))
        {
            errors.Add("Analytics:Provider must be anthropic or openai.");
        }

        foreach (var fallback in options.FallbackProviders)
        {
            if (!IsKnown(fallback))
            {
                errors.Add($"Analytics:FallbackProviders contains an unknown provider '{fallback}'.");
            }
        }

        if (options.PlannerOrder().Distinct(StringComparer.Ordinal).Count() != options.FallbackProviders.Count + 1)
        {
            errors.Add("Analytics:FallbackProviders must not repeat a provider or the primary one.");
        }

        if (options.Enabled)
        {
            foreach (var provider in options.PlannerOrder())
            {
                var model = provider == AnthropicAnalyticsQueryPlanner.ProviderId ? options.AnthropicModel : options.OpenAiModel;
                if (IsKnown(provider) && string.IsNullOrWhiteSpace(model))
                {
                    errors.Add($"Analytics: the model for provider '{provider}' is required when questions are enabled.");
                }
            }
        }

        if (!Enum.IsDefined(options.AnthropicEffort))
        {
            errors.Add("Analytics:AnthropicEffort must be Low, Medium or High.");
        }

        if (options.RequestTimeoutSeconds is < 1 or > 300)
        {
            errors.Add("Analytics:RequestTimeoutSeconds must be between 1 and 300.");
        }

        if (options.AnthropicMaximumRetries is < 0 or > 5)
        {
            errors.Add("Analytics:AnthropicMaximumRetries must be between 0 and 5.");
        }

        if (options.AnthropicMaximumOutputTokens is < 256 or > 64_000)
        {
            errors.Add("Analytics:AnthropicMaximumOutputTokens must be between 256 and 64000.");
        }

        if (options.OpenAiMaximumOutputTokens is < 256 or > 64_000)
        {
            errors.Add("Analytics:OpenAiMaximumOutputTokens must be between 256 and 64000.");
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    private static bool IsKnown(string? provider) => AnalyticsOptions.KnownProviders.Contains(AnalyticsOptions.Normalize(provider));
}
