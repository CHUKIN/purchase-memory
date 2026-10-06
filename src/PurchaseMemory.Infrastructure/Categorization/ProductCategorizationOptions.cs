using PurchaseMemory.Infrastructure.Recognition;

namespace PurchaseMemory.Infrastructure.Categorization;

/// <summary>
/// Settings for AI category suggestions (<c>Categorization__*</c>). The model is
/// deliberately a cheap one: the task is a short text classification, not
/// receipt reading.
/// </summary>
public sealed class ProductCategorizationOptions
{
    public const string SectionName = "Categorization";

    /// <summary>Off switch for suggestions after a save and for the backfill.</summary>
    public bool Enabled { get; set; } = true;

    public string Model { get; set; } = "gpt-6-luna";

    /// <summary>Products per request; the category list is repeated in every request.</summary>
    public int BatchSize { get; set; } = 40;

    public int RequestTimeoutSeconds { get; set; } = 60;

    public int MaximumOutputTokens { get; set; } = 8_000;

    /// <summary>
    /// Low is what gpt-6-luna was measured with on receipts. Not sent when
    /// <c>null</c>, for models without reasoning controls.
    /// </summary>
    public OpenAiReasoningEffort? ReasoningEffort { get; set; } = OpenAiReasoningEffort.Low;
}
