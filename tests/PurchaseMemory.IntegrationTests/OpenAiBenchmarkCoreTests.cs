using PurchaseMemory.Application.Receipts;

namespace PurchaseMemory.IntegrationTests;

public sealed class OpenAiBenchmarkCoreTests
{
    [Fact]
    public void ParsesModelsReceiptsAndDefaults()
    {
        var arguments = OpenAiArguments.Parse(["--models", "gpt-6-luna, gpt-6-sol", "--receipts", "01,03"]);

        Assert.Equal(["gpt-6-luna", "gpt-6-sol"], arguments.Models);
        Assert.Equal(["01", "03"], arguments.Receipts);
        Assert.Equal("high", arguments.ImageDetail);
        Assert.Equal("low", arguments.ReasoningEffort);
    }

    [Theory]
    [InlineData("")]
    [InlineData("--models m --detail ultra")]
    [InlineData("--models m --reasoning max")]
    [InlineData("--models m --api-key x")]
    [InlineData("--models")]
    public void RejectsInvalidArguments(string commandLine) =>
        Assert.Throws<ArgumentException>(() => OpenAiArguments.Parse(
            commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries)));

    [Fact]
    public void CostUsesListPricesPerMillionTokens()
    {
        Assert.Equal(0.0012m, OpenAiPrices.Cost("gpt-5.4-mini", 1000, 100));
        Assert.Equal(0.6m, OpenAiPrices.Cost("gpt-6-astra", 10_000, 10_000));
        Assert.Null(OpenAiPrices.Cost("unknown-model", 1, 1));
    }

    [Fact]
    public async Task UsageSumsEveryCallAndResets()
    {
        var provider = new UsageCountingVisionProvider(new FakeProvider());
        var request = new StructuredVisionRequest([new ReceiptImageData(new byte[] { 1 }, "image/jpeg")], "p", "{}", "1", "1", "c");

        await provider.GenerateAsync(request, CancellationToken.None);
        await provider.GenerateAsync(request, CancellationToken.None);
        var usage = provider.TakeUsage();

        Assert.Equal(2, usage.Calls);
        Assert.Equal(200, usage.InputTokens);
        Assert.Equal(20, usage.OutputTokens);
        Assert.Equal(["r1", "r2"], usage.RequestIds);
        Assert.Equal(0, provider.TakeUsage().Calls);
    }

    [Fact]
    public async Task FailedCallIsCountedWithoutTokens()
    {
        var provider = new UsageCountingVisionProvider(new FakeProvider(fail: true));
        var request = new StructuredVisionRequest([new ReceiptImageData(new byte[] { 1 }, "image/jpeg")], "p", "{}", "1", "1", "c");

        await Assert.ThrowsAsync<ReceiptExtractionException>(() => provider.GenerateAsync(request, CancellationToken.None));
        var usage = provider.TakeUsage();

        Assert.Equal(1, usage.Calls);
        Assert.Equal(0, usage.InputTokens);
    }

    private sealed class FakeProvider(bool fail = false) : IStructuredVisionProvider
    {
        private int _count;

        public string Id => "openai";

        public Task<StructuredVisionResponse> GenerateAsync(StructuredVisionRequest request, CancellationToken cancellationToken)
        {
            if (fail)
            {
                throw new ReceiptExtractionException("boom");
            }

            _count++;
            return Task.FromResult(new StructuredVisionResponse("{}", Id, "m", $"r{_count}", "completed", 100, 10, 5));
        }

        public Task<StructuredVisionProviderStatus> GetStatusAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new StructuredVisionProviderStatus(true, Id, ["m"], null));
    }
}
