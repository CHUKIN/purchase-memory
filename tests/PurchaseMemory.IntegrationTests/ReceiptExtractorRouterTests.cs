using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PurchaseMemory.Application.Receipts;
using PurchaseMemory.Infrastructure.Recognition;

namespace PurchaseMemory.IntegrationTests;

public sealed class ReceiptExtractorRouterTests
{
    [Fact]
    public async Task SelectsPipelineByConfigurationIdentifier()
    {
        var apple = FakePipeline.Available("apple", "Apple");
        var cloud = FakePipeline.Available("cloud", "Cloud");
        var router = CreateRouter(
            [apple, cloud],
            pipeline: "cloud",
            fallbacks: []);
        var result = await router.ExtractAsync(
            [new ReceiptImageData(new byte[] { 1, 2, 3 }, "image/jpeg")],
            TestContext.Current.CancellationToken);

        Assert.Equal("Cloud", result.ExtractorName);
        Assert.Equal(0, apple.ExtractionCalls);
        Assert.Equal(1, cloud.ExtractionCalls);
    }

    [Fact]
    public async Task ReplaysImageForFallbackWhenPrimaryRejectsReceipt()
    {
        var apple = FakePipeline.Failing("apple");
        var ollama = FakePipeline.Available("ollama", "Ollama");
        var router = CreateRouter(
            [apple, ollama],
            pipeline: "apple",
            fallbacks: ["ollama"]);
        var expectedImage = new byte[] { 9, 8, 7, 6 };
        var result = await router.ExtractAsync(
            [new ReceiptImageData(expectedImage, "image/png")],
            TestContext.Current.CancellationToken);

        Assert.Equal("Ollama", result.ExtractorName);
        Assert.Equal(expectedImage, apple.ObservedImage);
        Assert.Equal(expectedImage, ollama.ObservedImage);
    }

    [Fact]
    public async Task StatusReportsAvailableFallbackWhenPrimaryIsUnavailable()
    {
        var apple = FakePipeline.Unavailable("apple", "Sidecar is missing.");
        var ollama = FakePipeline.Available("ollama", "Ollama");
        var router = CreateRouter(
            [apple, ollama],
            pipeline: "apple",
            fallbacks: ["ollama"]);

        var status = await router.GetStatusAsync(TestContext.Current.CancellationToken);

        Assert.True(status.Available);
        Assert.Equal("ollama fallback", status.Mode);
        Assert.Contains("apple", status.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StatusDoesNotProbeFallbackWhenPrimaryIsAvailable()
    {
        var openAi = FakePipeline.Available("openai", "OpenAI");
        var ollama = FakePipeline.Unavailable("ollama", "Ollama is not reachable on the configured address.");
        var router = CreateRouter(
            [openAi, ollama],
            pipeline: "openai",
            fallbacks: ["ollama"]);

        var status = await router.GetStatusAsync(TestContext.Current.CancellationToken);

        Assert.True(status.Available);
        Assert.Equal("openai · fallback: ollama", status.Mode);
        Assert.Equal(["OpenAI"], status.Models);
        Assert.Null(status.Message);
        Assert.Equal(1, openAi.StatusCalls);
        Assert.Equal(0, ollama.StatusCalls);
    }

    [Fact]
    public async Task StatusReportsEveryPipelineWhenNoneIsAvailable()
    {
        var openAi = FakePipeline.Unavailable("openai", "Set OPENAI_API_KEY on the backend.");
        var ollama = FakePipeline.Unavailable("ollama", "Ollama is not reachable on the configured address.");
        var router = CreateRouter(
            [openAi, ollama],
            pipeline: "openai",
            fallbacks: ["ollama"]);

        var status = await router.GetStatusAsync(TestContext.Current.CancellationToken);

        Assert.False(status.Available);
        Assert.Equal("openai -> ollama", status.Mode);
        Assert.Empty(status.Models);
        Assert.Equal(
            "openai: Set OPENAI_API_KEY on the backend. ollama: Ollama is not reachable on the configured address.",
            status.Message);
        Assert.Equal(1, openAi.StatusCalls);
        Assert.Equal(1, ollama.StatusCalls);
    }

    private static ReceiptExtractorRouter CreateRouter(
        IEnumerable<IReceiptRecognitionPipeline> pipelines,
        string pipeline,
        List<string> fallbacks)
    {
        return new ReceiptExtractorRouter(
            pipelines,
            Options.Create(new ReceiptRecognitionOptions
            {
                Pipeline = pipeline,
                FallbackPipelines = fallbacks
            }),
            NullLogger<ReceiptExtractorRouter>.Instance);
    }

    private sealed class FakePipeline : IReceiptRecognitionPipeline
    {
        private readonly ReceiptExtractionResult _result;
        private readonly ReceiptRecognitionStatus _status;
        private readonly bool _failExtraction;

        private FakePipeline(
            string id,
            ReceiptExtractionResult result,
            ReceiptRecognitionStatus status,
            bool failExtraction)
        {
            Id = id;
            _result = result;
            _status = status;
            _failExtraction = failExtraction;
        }

        public string Id { get; }

        public int ExtractionCalls { get; private set; }

        public int StatusCalls { get; private set; }

        public byte[]? ObservedImage { get; private set; }

        public static FakePipeline Available(string id, string extractorName) =>
            new(
                id,
                CreateResult(extractorName),
                new ReceiptRecognitionStatus(true, id, [extractorName], null),
                failExtraction: false);

        public static FakePipeline Failing(string id) =>
            new(
                id,
                CreateResult(id),
                new ReceiptRecognitionStatus(true, id, [id], null),
                failExtraction: true);

        public static FakePipeline Unavailable(string id, string message) =>
            new(
                id,
                CreateResult(id),
                new ReceiptRecognitionStatus(false, id, [], message),
                failExtraction: true);

        public async Task<ReceiptExtractionResult> ExtractAsync(
            IReadOnlyList<ReceiptImageData> images,
            CancellationToken cancellationToken)
        {
            ExtractionCalls++;
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            ObservedImage = images[0].Content.ToArray();

            if (_failExtraction)
            {
                throw new ReceiptExtractionException($"{Id} rejected the receipt.");
            }

            return _result;
        }

        public Task<ReceiptRecognitionStatus> GetStatusAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StatusCalls++;
            return Task.FromResult(_status);
        }

        private static ReceiptExtractionResult CreateResult(string extractorName) =>
            new(
                "Test shop",
                null,
                "PLN",
                1m,
                [new ReceiptExtractionItem("Test item", 1m, "pcs", 1m, 1m)],
                extractorName,
                null);
    }
}
