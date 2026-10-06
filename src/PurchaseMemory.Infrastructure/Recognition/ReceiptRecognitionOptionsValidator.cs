using Microsoft.Extensions.Options;

namespace PurchaseMemory.Infrastructure.Recognition;

public sealed class ReceiptRecognitionOptionsValidator : IValidateOptions<ReceiptRecognitionOptions>
{
    // Conservative OpenAI generation speed used to sanity-check the timeout
    // against the output budget (measured gpt-6-sol: ~100 tokens/s).
    private const int MinimumOpenAiTokensPerSecond = 100;

    private readonly HashSet<string> _knownPipelines;

    public ReceiptRecognitionOptionsValidator(
        IEnumerable<ReceiptRecognitionPipelineDescriptor> pipelineDescriptors)
    {
        _knownPipelines = pipelineDescriptors
            .Select(descriptor => descriptor.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public ValidateOptionsResult Validate(string? name, ReceiptRecognitionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var errors = new List<string>();

        ValidatePipeline(options.Pipeline, "Pipeline", errors);
        foreach (var fallback in options.FallbackPipelines ?? [])
        {
            ValidatePipeline(fallback, "FallbackPipelines", errors);
        }

        if (!Uri.TryCreate(options.BaseAddress, UriKind.Absolute, out var baseAddress)
            || baseAddress.Scheme is not ("http" or "https"))
        {
            errors.Add("BaseAddress must be an absolute HTTP or HTTPS URI.");
        }

        if (options.RequestTimeoutSeconds is <= 0 or > 1800)
        {
            errors.Add("RequestTimeoutSeconds must be between 1 and 1800.");
        }

        if (options.AppleRequestTimeoutSeconds is <= 0 or > 600)
        {
            errors.Add("AppleRequestTimeoutSeconds must be between 1 and 600.");
        }

        if (options.ContextLength <= 0)
        {
            errors.Add("ContextLength must be greater than zero.");
        }

        if (options.MaximumOutputTokens <= 0)
        {
            errors.Add("MaximumOutputTokens must be greater than zero.");
        }
        else if (options.ContextLength > 0 && options.MaximumOutputTokens >= options.ContextLength)
        {
            errors.Add(
                "MaximumOutputTokens must be less than ContextLength: the output budget "
                + "is taken from the same context window as the prompt and the image.");
        }

        if (UsesPipeline(options, "openai"))
        {
            RequireModel(options.OpenAiModel, "OpenAiModel", errors);
        }

        if (options.OpenAiRequestTimeoutSeconds is <= 0 or > 600)
        {
            errors.Add("OpenAiRequestTimeoutSeconds must be between 1 and 600.");
        }

        if (options.OpenAiMaximumOutputTokens is <= 0 or > 128_000)
        {
            errors.Add("OpenAiMaximumOutputTokens must be between 1 and 128000.");
        }
        else if (options.OpenAiRequestTimeoutSeconds > 0
            && options.OpenAiMaximumOutputTokens / options.OpenAiRequestTimeoutSeconds > MinimumOpenAiTokensPerSecond)
        {
            // A timeout shorter than the time the output budget needs turns a
            // long, valid answer into a timeout error.
            errors.Add(
                $"OpenAiRequestTimeoutSeconds is too short for OpenAiMaximumOutputTokens: allow at least "
                + $"1 second per {MinimumOpenAiTokensPerSecond} output tokens.");
        }

        if (UsesPipeline(options, AppleGeometryOllamaExtractor.PipelineId))
        {
            RequireModel(options.RowsParserModel, "RowsParserModel", errors);

            if (options.RowsParserMaximumOutputTokens <= 0)
            {
                errors.Add("RowsParserMaximumOutputTokens must be greater than zero.");
            }
            else if (options.ContextLength > 0
                && options.RowsParserMaximumOutputTokens >= options.ContextLength)
            {
                errors.Add(
                    "RowsParserMaximumOutputTokens must be less than ContextLength: the output "
                    + "budget is taken from the same context window as the OCR rows.");
            }

            if (options.RowsParserRequestTimeoutSeconds is <= 0 or > 1800)
            {
                errors.Add("RowsParserRequestTimeoutSeconds must be between 1 and 1800.");
            }
        }

        if (options.Mode == ReceiptRecognitionMode.Hybrid)
        {
            RequireModel(options.OcrModel, "OcrModel", errors);
            RequireModel(options.ParserModel, "ParserModel", errors);
        }
        else
        {
            RequireModel(options.VisionModel, "VisionModel", errors);
        }

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }

    private static bool UsesPipeline(ReceiptRecognitionOptions options, string pipelineId) =>
        string.Equals(options.Pipeline?.Trim(), pipelineId, StringComparison.OrdinalIgnoreCase)
        || (options.FallbackPipelines ?? []).Any(
            value => string.Equals(value?.Trim(), pipelineId, StringComparison.OrdinalIgnoreCase));

    private void ValidatePipeline(
        string? pipeline,
        string property,
        List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(pipeline))
        {
            errors.Add($"{property} must not contain an empty provider identifier.");
        }
        else if (!_knownPipelines.Contains(pipeline.Trim()))
        {
            errors.Add($"{property} contains unknown provider '{pipeline}'.");
        }
    }

    private static void RequireModel(
        string? model,
        string property,
        List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            errors.Add($"{property} must not be empty.");
        }
    }
}

/// <summary>
/// Startup-safe catalog entry used by options validation. Adding a provider
/// requires registering its descriptor beside its adapter, not editing the
/// validator.
/// </summary>
public sealed record ReceiptRecognitionPipelineDescriptor(string Id);
