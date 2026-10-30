using Microsoft.Extensions.Options;
using PurchaseMemory.Infrastructure.Recognition;

namespace PurchaseMemory.Infrastructure.PhotoNames;

/// <summary>
/// Settings for reading a product name from a package photo
/// (<c>ProductPhotoNaming__*</c>). The credential is the same
/// <c>ANTHROPIC_API_KEY</c> receipt recognition uses, read from the
/// environment only.
/// </summary>
public sealed class ProductPhotoNamingOptions
{
    public const string SectionName = "ProductPhotoNaming";

    /// <summary>Off switch; when off the endpoint answers 503 «unavailable».</summary>
    public bool Enabled { get; set; } = true;

    public string Model { get; set; } = "claude-haiku-5-5";

    /// <summary>Reading a package is easy for a vision model; low is enough.</summary>
    public AnthropicEffort Effort { get; set; } = AnthropicEffort.Low;

    /// <summary>The person is on the product page; a photo call is a little slower than text.</summary>
    public int RequestTimeoutSeconds { get; set; } = 45;

    public int MaximumRetries { get; set; } = 1;

    /// <summary>Room for adaptive thinking plus six short fields.</summary>
    public int MaximumOutputTokens { get; set; } = 4_000;
}

/// <summary>Rejects bad <c>ProductPhotoNaming:*</c> values at startup (ValidateOnStart).</summary>
public sealed class ProductPhotoNamingOptionsValidator : IValidateOptions<ProductPhotoNamingOptions>
{
    public ValidateOptionsResult Validate(string? name, ProductPhotoNamingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var errors = new List<string>();
        if (options.Enabled && string.IsNullOrWhiteSpace(options.Model))
        {
            errors.Add("ProductPhotoNaming:Model is required when naming from photos is enabled.");
        }

        if (!Enum.IsDefined(options.Effort))
        {
            errors.Add("ProductPhotoNaming:Effort must be Low, Medium or High.");
        }

        if (options.RequestTimeoutSeconds is < 1 or > 300)
        {
            errors.Add("ProductPhotoNaming:RequestTimeoutSeconds must be between 1 and 300.");
        }

        if (options.MaximumRetries is < 0 or > 5)
        {
            errors.Add("ProductPhotoNaming:MaximumRetries must be between 0 and 5.");
        }

        if (options.MaximumOutputTokens is < 256 or > 64_000)
        {
            errors.Add("ProductPhotoNaming:MaximumOutputTokens must be between 256 and 64000.");
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
