using Microsoft.Extensions.Options;
using PurchaseMemory.Infrastructure.Recognition;

namespace PurchaseMemory.Infrastructure.HaulPhoto;

/// <summary>
/// Settings for «Сфотографировать покупки» (<c>HaulPhoto__*</c>). The
/// credential is the same <c>ANTHROPIC_API_KEY</c> the other Claude calls use,
/// read from the environment only.
/// </summary>
public sealed class HaulPhotoOptions
{
    public const string SectionName = "HaulPhoto";

    /// <summary>
    /// Off by default: the hit rate is not measured yet. When off both
    /// endpoints answer 503 «haulPhotoDisabled» and the app hides the button.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Sonnet, not Haiku: finding 10-30 items on one table photo, boxing each
    /// and telling apart look-alike lines («JOGURT NAT» vs «JOGURT GRECKI») is
    /// spatial and comparative work where Haiku is weakest, and the call runs
    /// once per shopping trip, so a few cents buy a lot. The benchmark takes
    /// <c>--model</c> to compare Haiku on the same photos.
    /// </summary>
    public string Model { get; set; } = "claude-sonnet-5-5";

    public AnthropicEffort Effort { get; set; } = AnthropicEffort.Medium;

    /// <summary>The person waits at the table, but a big table takes the model a while.</summary>
    public int RequestTimeoutSeconds { get; set; } = 90;

    public int MaximumRetries { get; set; } = 1;

    /// <summary>Room for thinking plus up to 40 boxed items (~40 tokens each).</summary>
    public int MaximumOutputTokens { get; set; } = 8_000;
}

/// <summary>Rejects bad <c>HaulPhoto:*</c> values at startup (ValidateOnStart).</summary>
public sealed class HaulPhotoOptionsValidator : IValidateOptions<HaulPhotoOptions>
{
    public ValidateOptionsResult Validate(string? name, HaulPhotoOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var errors = new List<string>();
        if (options.Enabled && string.IsNullOrWhiteSpace(options.Model))
        {
            errors.Add("HaulPhoto:Model is required when photos of the purchased items are enabled.");
        }

        if (!Enum.IsDefined(options.Effort))
        {
            errors.Add("HaulPhoto:Effort must be Low, Medium or High.");
        }

        if (options.RequestTimeoutSeconds is < 1 or > 300)
        {
            errors.Add("HaulPhoto:RequestTimeoutSeconds must be between 1 and 300.");
        }

        if (options.MaximumRetries is < 0 or > 5)
        {
            errors.Add("HaulPhoto:MaximumRetries must be between 0 and 5.");
        }

        if (options.MaximumOutputTokens is < 1_024 or > 64_000)
        {
            errors.Add("HaulPhoto:MaximumOutputTokens must be between 1024 and 64000.");
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
