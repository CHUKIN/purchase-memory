using Microsoft.Extensions.Configuration;
using PurchaseMemory.Infrastructure.Recognition;

namespace PurchaseMemory.IntegrationTests;

public sealed class ReceiptRecognitionOptionsValidatorTests
{
    private readonly ReceiptRecognitionOptionsValidator _validator =
        new([new ReceiptRecognitionPipelineDescriptor("apple"),
             new ReceiptRecognitionPipelineDescriptor("ollama"),
             new ReceiptRecognitionPipelineDescriptor("openai"),
             new ReceiptRecognitionPipelineDescriptor(AnthropicStructuredVisionProvider.ProviderId),
             new ReceiptRecognitionPipelineDescriptor(AppleGeometryOllamaExtractor.PipelineId)]);

    [Fact]
    public void RejectsUnknownProvider()
    {
        var options = ValidOptions();
        options.Pipeline = "unknown";

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("unknown provider", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsEmptyModelAndInvalidTimeout()
    {
        var options = ValidOptions();
        options.ParserModel = " ";
        options.RequestTimeoutSeconds = 0;

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("ParserModel", StringComparison.Ordinal));
        Assert.Contains(result.Failures, failure => failure.Contains("RequestTimeoutSeconds", StringComparison.Ordinal));
    }

    [Fact]
    public void AcceptsRegisteredProviderAndValidModels()
    {
        Assert.True(_validator.Validate(null, ValidOptions()).Succeeded);
    }

    [Fact]
    public void OpenAiRouteRequiresModelAndValidProviderLimits()
    {
        var options = ValidOptions();
        options.Pipeline = "openai";
        options.OpenAiModel = " ";
        options.OpenAiRequestTimeoutSeconds = 0;
        options.OpenAiMaximumOutputTokens = 0;

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("OpenAiModel", StringComparison.Ordinal));
        Assert.Contains(result.Failures, failure => failure.Contains("OpenAiRequestTimeoutSeconds", StringComparison.Ordinal));
        Assert.Contains(result.Failures, failure => failure.Contains("OpenAiMaximumOutputTokens", StringComparison.Ordinal));
    }

    [Fact]
    public void AnthropicRouteRequiresModelAndValidProviderLimits()
    {
        var options = ValidOptions();
        options.Pipeline = "anthropic";
        options.FallbackPipelines = ["openai"];
        options.OpenAiModel = "gpt-6-sol";
        options.AnthropicModel = " ";
        options.AnthropicRequestTimeoutSeconds = 0;
        options.AnthropicMaximumOutputTokens = 0;
        options.AnthropicMaximumRetries = -1;
        options.AnthropicReceiptContract = (PurchaseMemory.Application.Receipts.ReceiptAnalysisContractVersion)99;

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("AnthropicModel", StringComparison.Ordinal));
        Assert.Contains(result.Failures, failure => failure.Contains("AnthropicRequestTimeoutSeconds", StringComparison.Ordinal));
        Assert.Contains(result.Failures, failure => failure.Contains("AnthropicMaximumOutputTokens", StringComparison.Ordinal));
        Assert.Contains(result.Failures, failure => failure.Contains("AnthropicMaximumRetries", StringComparison.Ordinal));
        Assert.Contains(result.Failures, failure => failure.Contains("AnthropicReceiptContract", StringComparison.Ordinal));
    }

    [Fact]
    public void AnthropicTimeoutMustCoverTheOutputBudget()
    {
        var options = ValidOptions();
        options.Pipeline = "anthropic";
        options.AnthropicModel = "claude-haiku-5-5";
        options.AnthropicMaximumOutputTokens = 16_384;
        options.AnthropicRequestTimeoutSeconds = 30;

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("too short for AnthropicMaximumOutputTokens", StringComparison.Ordinal));
    }

    [Fact]
    public void AnthropicModelIsNotRequiredWhenAnthropicIsNotInTheRoute()
    {
        var options = ValidOptions();
        options.AnthropicModel = "";

        Assert.True(_validator.Validate(null, options).Succeeded);
    }

    [Fact]
    public void FallbackPipelinesFromConfigurationReplaceTheDefault()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ReceiptRecognition:Pipeline"] = "openai",
                ["ReceiptRecognition:FallbackPipelines:0"] = "ollama"
            })
            .Build();
        var options = new ReceiptRecognitionOptions();

        configuration.GetSection(ReceiptRecognitionOptions.SectionName).Bind(options);

        Assert.Equal("openai", options.Pipeline);
        Assert.Equal(["ollama"], options.FallbackPipelines);
    }

    [Fact]
    public void DefaultOptionsAreValid()
    {
        // Track B: the default primary pipeline is direct vision-LLM via
        // Ollama (qwen3-vl:8b-instruct, Vision mode, no OCR pass). Fallbacks
        // come from configuration only, because binding appends to a
        // non-empty default list (see FallbackPipelinesFromConfigurationReplaceTheDefault).
        var options = new ReceiptRecognitionOptions();

        var result = _validator.Validate(null, options);

        Assert.True(result.Succeeded);
        Assert.Equal("ollama", options.Pipeline);
        Assert.Empty(options.FallbackPipelines);
        Assert.Equal(ReceiptRecognitionMode.Vision, options.Mode);
        Assert.Equal("qwen3-vl:8b-instruct", options.VisionModel);
    }

    [Fact]
    public void DefaultGenerationBudgetsMatchDenseReceiptSettings()
    {
        // Stage 0: dense receipts (48 line items) truncated the model response
        // at 2048 tokens, so the output budget is 8192 inside a 32768 window,
        // and the request timeout covers the four-fold longer generation.
        var options = new ReceiptRecognitionOptions();

        Assert.Equal(8_192, options.MaximumOutputTokens);
        // Prompt v3 adds rawLines to the answer, so OpenAI gets 16384 and a
        // timeout long enough to generate all of it.
        Assert.Equal(16_384, options.OpenAiMaximumOutputTokens);
        Assert.Equal(300, options.OpenAiRequestTimeoutSeconds);
        Assert.Equal(32_768, options.ContextLength);
        Assert.Equal(600, options.RequestTimeoutSeconds);

        var explicitOptions = ValidOptions();
        explicitOptions.ContextLength = 32_768;
        explicitOptions.MaximumOutputTokens = 8_192;

        Assert.True(_validator.Validate(null, explicitOptions).Succeeded);
    }

    [Fact]
    public void RejectsMaximumOutputTokensThatDoNotFitInsideContextLength()
    {
        var options = ValidOptions();
        options.ContextLength = 8_192;
        options.MaximumOutputTokens = 8_192;

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(
            result.Failures,
            failure => failure.Contains("MaximumOutputTokens must be less than ContextLength", StringComparison.Ordinal));
    }

    [Fact]
    public void AcceptsRequestTimeoutUpToUpperBoundAndRejectsAbove()
    {
        var atBound = ValidOptions();
        atBound.RequestTimeoutSeconds = 1_800;

        Assert.True(_validator.Validate(null, atBound).Succeeded);

        var aboveBound = ValidOptions();
        aboveBound.RequestTimeoutSeconds = 1_801;

        var result = _validator.Validate(null, aboveBound);

        Assert.True(result.Failed);
        Assert.Contains(
            result.Failures,
            failure => failure.Contains("RequestTimeoutSeconds must be between 1 and 1800", StringComparison.Ordinal));
    }

    [Fact]
    public void RowsParserDefaultsAreValidForTheAppleOllamaPipeline()
    {
        // 16384 inside a 32768 window and a 1800 s ceiling are the values
        // research/ocr-geometry/drafts/parse.py was measured with.
        var defaults = new ReceiptRecognitionOptions();

        Assert.Equal("qwen3.5:35b", defaults.RowsParserModel);
        Assert.Equal(16_384, defaults.RowsParserMaximumOutputTokens);
        Assert.Equal(1_800, defaults.RowsParserRequestTimeoutSeconds);
        Assert.True(_validator.Validate(null, AppleOllamaOptions()).Succeeded);
    }

    [Fact]
    public void RejectsRowsParserBudgetThatDoesNotFitInsideContextLength()
    {
        var options = AppleOllamaOptions();
        options.ContextLength = 16_384;
        options.RowsParserMaximumOutputTokens = 16_384;

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(
            result.Failures,
            failure => failure.Contains(
                "RowsParserMaximumOutputTokens must be less than ContextLength",
                StringComparison.Ordinal));

        var zeroBudget = AppleOllamaOptions();
        zeroBudget.RowsParserMaximumOutputTokens = 0;
        var zeroResult = _validator.Validate(null, zeroBudget);

        Assert.True(zeroResult.Failed);
        Assert.Contains(
            zeroResult.Failures!,
            failure => failure.Contains(
                "RowsParserMaximumOutputTokens must be greater than zero",
                StringComparison.Ordinal));
    }

    [Fact]
    public void AcceptsRowsParserTimeoutUpToUpperBoundAndRejectsAbove()
    {
        var atBound = AppleOllamaOptions();
        atBound.RowsParserRequestTimeoutSeconds = 1_800;

        Assert.True(_validator.Validate(null, atBound).Succeeded);

        var aboveBound = AppleOllamaOptions();
        aboveBound.RowsParserRequestTimeoutSeconds = 1_801;

        var result = _validator.Validate(null, aboveBound);

        Assert.True(result.Failed);
        Assert.Contains(
            result.Failures,
            failure => failure.Contains(
                "RowsParserRequestTimeoutSeconds must be between 1 and 1800",
                StringComparison.Ordinal));
    }

    [Fact]
    public void RowsParserSettingsAreIgnoredWhenTheAppleOllamaPipelineIsNotInTheRoute()
    {
        // The gate matters as much as the rule: stage 0 shipped a validator that
        // failed configurations it had no business judging. Deliberate garbage in
        // every RowsParser* value must stay valid while apple-ollama is unused.
        var options = ValidOptions();
        options.RowsParserModel = " ";
        options.RowsParserMaximumOutputTokens = -1;
        options.RowsParserRequestTimeoutSeconds = 99_999;

        Assert.True(_validator.Validate(null, options).Succeeded);
    }

    [Fact]
    public void RowsParserSettingsAreCheckedWhenTheAppleOllamaPipelineIsOnlyAFallback()
    {
        var options = ValidOptions();
        options.FallbackPipelines = [AppleGeometryOllamaExtractor.PipelineId];
        options.RowsParserModel = " ";
        options.RowsParserRequestTimeoutSeconds = 99_999;

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("RowsParserModel", StringComparison.Ordinal));
        Assert.Contains(
            result.Failures,
            failure => failure.Contains("RowsParserRequestTimeoutSeconds", StringComparison.Ordinal));
    }

    private static ReceiptRecognitionOptions AppleOllamaOptions() => new()
    {
        Pipeline = AppleGeometryOllamaExtractor.PipelineId,
        FallbackPipelines = [],
        Mode = ReceiptRecognitionMode.Vision,
        VisionModel = "vision",
        BaseAddress = "http://127.0.0.1:11434",
        RequestTimeoutSeconds = 120
    };

    private static ReceiptRecognitionOptions ValidOptions() => new()
    {
        Pipeline = "ollama",
        FallbackPipelines = [],
        Mode = ReceiptRecognitionMode.Hybrid,
        OcrModel = "ocr",
        ParserModel = "parser",
        BaseAddress = "http://127.0.0.1:11434",
        RequestTimeoutSeconds = 120
    };
}
