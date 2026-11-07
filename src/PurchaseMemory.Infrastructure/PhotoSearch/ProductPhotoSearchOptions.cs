using Microsoft.Extensions.Options;
using PurchaseMemory.Infrastructure.Recognition;

namespace PurchaseMemory.Infrastructure.PhotoSearch;

/// <summary>
/// Settings for «Найти по фото» (<c>ProductPhotoSearch__*</c>). The credential
/// is the same <c>ANTHROPIC_API_KEY</c> the other Claude calls use, read from
/// the environment only.
/// </summary>
public sealed class ProductPhotoSearchOptions
{
    public const string SectionName = "ProductPhotoSearch";

    /// <summary>
    /// Off by default: the hit rate is not measured yet. When off the endpoint
    /// answers 503 «findByPhotoDisabled» and the app hides the button.
    /// </summary>
    public bool Enabled { get; set; }

    public string Model { get; set; } = "claude-haiku-5-5";

    /// <summary>Picking from a short list is easy once the photo is seen; low is the starting point to measure.</summary>
    public AnthropicEffort Effort { get; set; } = AnthropicEffort.Low;

    /// <summary>The person is standing in the shop: a slow answer is as bad as none.</summary>
    public int RequestTimeoutSeconds { get; set; } = 45;

    public int MaximumRetries { get; set; } = 1;

    /// <summary>Room for adaptive thinking plus a three-item answer.</summary>
    public int MaximumOutputTokens { get; set; } = 4_000;

    /// <summary>
    /// The cap on the candidate list. One line is ~30-45 input tokens (Polish,
    /// Russian and receipt names, brand, category), so 300 lines are ~12k
    /// tokens: with the ~1k-token photo about $0.0015 per search on Haiku.
    /// </summary>
    public int MaximumCandidates { get; set; } = 300;
}

/// <summary>Rejects bad <c>ProductPhotoSearch:*</c> values at startup (ValidateOnStart).</summary>
public sealed class ProductPhotoSearchOptionsValidator : IValidateOptions<ProductPhotoSearchOptions>
{
    public ValidateOptionsResult Validate(string? name, ProductPhotoSearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var errors = new List<string>();
        if (options.Enabled && string.IsNullOrWhiteSpace(options.Model))
        {
            errors.Add("ProductPhotoSearch:Model is required when finding products by photo is enabled.");
        }

        if (!Enum.IsDefined(options.Effort))
        {
            errors.Add("ProductPhotoSearch:Effort must be Low, Medium or High.");
        }

        if (options.RequestTimeoutSeconds is < 1 or > 300)
        {
            errors.Add("ProductPhotoSearch:RequestTimeoutSeconds must be between 1 and 300.");
        }

        if (options.MaximumRetries is < 0 or > 5)
        {
            errors.Add("ProductPhotoSearch:MaximumRetries must be between 0 and 5.");
        }

        if (options.MaximumOutputTokens is < 256 or > 64_000)
        {
            errors.Add("ProductPhotoSearch:MaximumOutputTokens must be between 256 and 64000.");
        }

        if (options.MaximumCandidates is < 10 or > 1_000)
        {
            errors.Add("ProductPhotoSearch:MaximumCandidates must be between 10 and 1000.");
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
