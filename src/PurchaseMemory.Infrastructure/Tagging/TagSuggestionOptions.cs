using Microsoft.Extensions.Options;
using PurchaseMemory.Infrastructure.Recognition;

namespace PurchaseMemory.Infrastructure.Tagging;

/// <summary>
/// Settings for AI tag suggestions (<c>TagSuggestions__*</c>). The credential
/// is the same <c>ANTHROPIC_API_KEY</c> receipt recognition uses; it is read
/// from the environment only.
/// </summary>
public sealed class TagSuggestionOptions
{
    public const string SectionName = "TagSuggestions";

    /// <summary>Off switch; when off the endpoint answers 503 «unavailable».</summary>
    public bool Enabled { get; set; } = true;

    public string Model { get; set; } = "claude-haiku-5-5";

    /// <summary>Choosing from a short list is an easy task; low is enough.</summary>
    public AnthropicEffort Effort { get; set; } = AnthropicEffort.Low;

    /// <summary>The person waits on the product page, so this stays short.</summary>
    public int RequestTimeoutSeconds { get; set; } = 30;

    public int MaximumRetries { get; set; } = 1;

    /// <summary>Room for adaptive thinking plus a handful of short reasons.</summary>
    public int MaximumOutputTokens { get; set; } = 4_000;

    /// <summary>Tags offered in one request; a household has far fewer.</summary>
    public int MaximumOfferedTags { get; set; } = 200;
}

/// <summary>
/// Rejects bad <c>TagSuggestions:*</c> values at startup (registered with
/// ValidateOnStart) instead of letting them surface as a 500 on first use.
/// </summary>
public sealed class TagSuggestionOptionsValidator : IValidateOptions<TagSuggestionOptions>
{
    public ValidateOptionsResult Validate(string? name, TagSuggestionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var errors = new List<string>();
        if (options.Enabled && string.IsNullOrWhiteSpace(options.Model))
        {
            errors.Add("TagSuggestions:Model is required when tag suggestions are enabled.");
        }

        if (!Enum.IsDefined(options.Effort))
        {
            errors.Add("TagSuggestions:Effort must be Low, Medium or High.");
        }

        if (options.RequestTimeoutSeconds is < 1 or > 300)
        {
            errors.Add("TagSuggestions:RequestTimeoutSeconds must be between 1 and 300.");
        }

        if (options.MaximumRetries is < 0 or > 5)
        {
            errors.Add("TagSuggestions:MaximumRetries must be between 0 and 5.");
        }

        if (options.MaximumOutputTokens is < 256 or > 64_000)
        {
            errors.Add("TagSuggestions:MaximumOutputTokens must be between 256 and 64000.");
        }

        if (options.MaximumOfferedTags is < 1 or > 1_000)
        {
            errors.Add("TagSuggestions:MaximumOfferedTags must be between 1 and 1000.");
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
