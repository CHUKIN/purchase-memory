namespace PurchaseMemory.Infrastructure.Recognition;

public enum ReceiptRecognitionMode
{
    Hybrid,
    Vision
}

public enum OpenAiImageDetail
{
    Low,
    High,
    Original
}

public enum OpenAiReasoningEffort
{
    Minimal,
    Low,
    Medium,
    High
}

public sealed class ReceiptRecognitionOptions
{
    public const string SectionName = "ReceiptRecognition";

    public string Pipeline { get; set; } = "ollama";

    /// <summary>
    /// Empty by default on purpose: the configuration binder appends list items
    /// to an initialized list instead of replacing it, so a code default of
    /// ["apple"] silently turned appsettings' ["ollama"] into apple → ollama.
    /// The fallback route is configuration only (appsettings.json).
    /// </summary>
    public List<string> FallbackPipelines { get; set; } = [];

    public ReceiptRecognitionMode Mode { get; set; } = ReceiptRecognitionMode.Vision;

    public string BaseAddress { get; set; } = "http://127.0.0.1:11434";

    public string AppleSidecarPath { get; set; } = ".artifacts/apple-intelligence/PurchaseMemory.AppleIntelligence";

    public int AppleRequestTimeoutSeconds { get; set; } = 45;

    public string OcrModel { get; set; } = "glm-ocr:latest";

    public string ParserModel { get; set; } = "qwen3.5:4b";

    public string VisionModel { get; set; } = "qwen3-vl:8b-instruct";

    /// <summary>
    /// The text model that turns Apple Vision OCR rows into a receipt draft on
    /// the <c>apple-ollama</c> pipeline. Configuration, never a constant: Ollama
    /// proxies its cloud models through the same local daemon, so moving the
    /// parser to the cloud must be a change of this value alone.
    /// </summary>
    public string RowsParserModel { get; set; } = "qwen3.5:35b";

    /// <summary>
    /// Output budget for the <c>apple-ollama</c> parser, kept separate from
    /// <see cref="MaximumOutputTokens"/>. 16384 is the value every measurement in
    /// ПЛАН_КОНВЕЙЕР_OCR_ГЕОМЕТРИЯ.md was taken with
    /// (<c>research/ocr-geometry/drafts/parse.py</c>): a 48-line receipt needs far
    /// more output than a short one, and a budget that runs out looks like a bad
    /// model rather than a bad setting. It is only a ceiling — short receipts
    /// generate the same number of tokens either way.
    /// </summary>
    public int RowsParserMaximumOutputTokens { get; set; } = 16_384;

    /// <summary>
    /// HTTP timeout for the <c>apple-ollama</c> parser call. Separate from
    /// <see cref="RequestTimeoutSeconds"/> because this route runs a much larger
    /// model over a much longer prompt — the densest receipt in the research set
    /// took 78 seconds locally — and the configured tag may be a cloud model with
    /// no latency guarantee at all. The research script used 1800.
    /// </summary>
    public int RowsParserRequestTimeoutSeconds { get; set; } = 1_800;

    public int RequestTimeoutSeconds { get; set; } = 600;

    /// <summary>
    /// When <c>true</c> (the default) the Ollama providers confirm through
    /// <c>/api/tags</c> (and <c>/api/show</c> for vision) that the configured model
    /// exists before the first request. The receipt benchmark can turn it off for
    /// the hosted API, whose model listing may name models differently from the
    /// local daemon's <c>:cloud</c> proxy tags; a missing model then surfaces as an
    /// HTTP error on the request itself.
    /// </summary>
    public bool VerifyModelAvailability { get; set; } = true;

    public int ContextLength { get; set; } = 32_768;

    public int MaximumOutputTokens { get; set; } = 8_192;

    public string OpenAiModel { get; set; } = "";

    public OpenAiImageDetail OpenAiImageDetail { get; set; } = OpenAiImageDetail.High;

    /// <summary>
    /// HTTP timeout for one OpenAI call. It has to cover the whole output
    /// budget: measured gpt-6-sol throughput is ~100 output tokens/s (≈2000
    /// tokens in ≈21 s per receipt), so a response that runs to the 16384-token
    /// ceiling needs ≈165 s plus reasoning and upload time. 300 s leaves that
    /// headroom; 90 s would cut off exactly the long receipts the larger budget
    /// exists for.
    /// </summary>
    public int OpenAiRequestTimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// Output ceiling for one OpenAI call. Prompt v3 asks for rawLines before
    /// items, which adds 40–100% output on long receipts; at 8192 some answers
    /// were cut off (РЕЗУЛЬТАТЫ_ЭКСПЕРИМЕНТ_ИНСТРУКЦИИ_ЧЕК_04.md). Only a
    /// ceiling: short receipts cost the same either way.
    /// </summary>
    public int OpenAiMaximumOutputTokens { get; set; } = 16_384;

    public OpenAiReasoningEffort OpenAiReasoningEffort { get; set; } = OpenAiReasoningEffort.Low;
}
