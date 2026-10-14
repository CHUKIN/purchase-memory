using System.Text.Json;
using System.Text.RegularExpressions;
using PurchaseMemory.Application.Receipts;
using PurchaseMemory.Domain.Receipts;
using PurchaseMemory.Infrastructure.Recognition;

namespace PurchaseMemory.IntegrationTests;

/// <summary>
/// Contract v4 ("full-structured", the anthropic pipeline's default): prompt
/// parity with the measured benchmark prompt, parsing of a recorded response,
/// mapping of discounts onto v3-shaped rows, tolerant boxes and the workflow's
/// contract selection. No network.
/// </summary>
public sealed partial class ReceiptStructuredContractTests
{
    // Recorded haiku-full-structured-medium answer for household receipt 01
    // (test-data/.../ocr-compare/runs/haiku-full-structured-medium), rebuilt in
    // the raw v4 shape: one discount on item 3, one on the whole receipt.
    private const string RecordedResponse = """
        {"merchantName":"Dodo Pizza Poland Sp. z o.o.","purchasedAt":"2026-03-21T16:39:00+01:00","currency":"PLN",
         "items":[
          {"name":"Dostawy 1 szt","quantity":1,"unit":null,"unitPrice":5.98,"lineTotal":5.98,"box":{"x":0.25,"y":0.345,"width":0.5,"height":0.03}},
          {"name":"Placki ziemniaczane 8 szt.","quantity":1,"unit":null,"unitPrice":17.99,"lineTotal":17.99,"box":{"x":0.25,"y":0.365,"width":0.5,"height":0.03}},
          {"name":"4 pory roku Średnia","quantity":1,"unit":null,"unitPrice":53.99,"lineTotal":53.99,"box":{"x":0.25,"y":0.382,"width":0.5,"height":0.03}},
          {"name":"Pudełko na pizzę 30 1 szt","quantity":1,"unit":"szt","unitPrice":2.01,"lineTotal":2.01,"box":{"x":0.25,"y":0.4,"width":0.5,"height":0.03}}],
         "discounts":[
          {"label":"OPUST","amount":-21.59,"appliesToItem":null,"box":{"x":0.27,"y":0.468,"width":0.5,"height":0.03}},
          {"label":"OPUST","amount":-2.0,"appliesToItem":3,"box":{"x":0.27,"y":0.415,"width":0.5,"height":0.03}}],
         "subtotal":77.97,"taxTotal":4.17,"receiptTotal":56.38,
         "receiptTotalBox":{"x":0.7,"y":0.565,"width":0.1,"height":0.035}}
        """;

    [Fact]
    public void SinglePhotoPromptIsExactlyTheMeasuredBenchmarkPrompt()
    {
        var benchmark = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "tools", "PurchaseMemory.ReceiptBenchmarks", "ocr_compare", "haiku_matrix.py"));
        var measured = FullPromptLiteral().Match(benchmark);
        Assert.True(measured.Success, "FULL_PROMPT not found in haiku_matrix.py");

        Assert.Equal(measured.Groups["text"].Value, ReceiptStructuredAnalysisContract.BuildPrompt(null));
    }

    [Fact]
    public void MultiPhotoNoteAndRepairAreAppendedOnlyWhenTheyApply()
    {
        var single = ReceiptStructuredAnalysisContract.BuildPrompt(null);
        var multi = ReceiptStructuredAnalysisContract.BuildPrompt("sprawdź rabat", ["$.discounts[0].amount is required."], imageCount: 2);

        Assert.DoesNotContain("kolejne części tego samego paragonu", single);
        Assert.StartsWith(single, multi, StringComparison.Ordinal);
        Assert.Contains("kolejne części tego samego paragonu", multi);
        Assert.Contains("userNote = " + JsonSerializer.Serialize("sprawdź rabat"), multi);
        Assert.Contains("- $.discounts[0].amount is required.", multi);
    }

    [Fact]
    public void SchemaNeedsNoAdaptationForClaude()
    {
        var adapted = JsonDocument.Parse(AnthropicJsonSchemaAdapter.AdaptToJson(ReceiptStructuredAnalysisContract.JsonSchema));
        var original = JsonDocument.Parse(ReceiptStructuredAnalysisContract.JsonSchema);

        Assert.True(JsonElement.DeepEquals(original.RootElement, adapted.RootElement));
    }

    [Fact]
    public void RecordedResponseMapsOntoTheV3RowShape()
    {
        var parsed = ReceiptStructuredOutputParser.ParseStructured(RecordedResponse);
        var extraction = parsed.Extraction;

        Assert.Empty(parsed.Warnings);
        Assert.Equal("Dodo Pizza Poland Sp. z o.o.", extraction.MerchantName);
        Assert.Equal(new DateTimeOffset(2026, 3, 21, 16, 39, 0, TimeSpan.FromHours(1)), extraction.PurchasedAt);
        Assert.Equal("PLN", extraction.Currency);
        Assert.Equal(56.38m, extraction.ReceiptTotal);
        Assert.Equal(0.7, extraction.ReceiptTotalBoundingBox!.X, 6);

        // Item-level discount right after its product; receipt-level one last.
        Assert.Equal(
            ["Dostawy 1 szt", "Placki ziemniaczane 8 szt.", "4 pory roku Średnia", "Pudełko na pizzę 30 1 szt", "OPUST", "OPUST"],
            extraction.Items.Select(item => item.Name));
        Assert.Equal([5.98m, 17.99m, 53.99m, 2.01m, -2.0m, -21.59m], extraction.Items.Select(item => item.LineTotal!.Value));
        Assert.Equal(
            [ReceiptLineKind.Product, ReceiptLineKind.Product, ReceiptLineKind.Product, ReceiptLineKind.Product, ReceiptLineKind.Discount, ReceiptLineKind.Discount],
            extraction.Items.Select(item => item.Kind));
        Assert.All(extraction.Items, item => Assert.NotNull(item.BoundingBox));
        Assert.Equal(0.415, extraction.Items[4].BoundingBox!.Y, 6);
        Assert.Equal(0.468, extraction.Items[5].BoundingBox!.Y, 6);
        Assert.Equal("szt", extraction.Items[3].Unit);
        Assert.Equal(extraction.ReceiptTotal, extraction.Items.Sum(item => item.LineTotal));
    }

    [Fact]
    public void MappedDiscountsLinkLikeTodaysRowsAfterPostprocessing()
    {
        var extraction = ReceiptExtractionPostprocessor.Apply(
            ReceiptStructuredOutputParser.ParseStructured(RecordedResponse).Extraction);

        var links = ReceiptDiscountLinking.Resolve(extraction.Items
            .Select(item => new ReceiptLineForLinking(item.Kind, item.Name, item.LineTotal))
            .ToList());

        Assert.Equal(new ReceiptDiscountLink(3, false), links[4]);
        // -21.59 is larger than the product before it, so it belongs to the receipt.
        Assert.Equal(new ReceiptDiscountLink(null, true), links[5]);
        Assert.Equal(2, extraction.Items.Count(item => item.Kind == ReceiptLineKind.Discount));
    }

    [Fact]
    public void DiscountSignLabelAndIndexAreNormalized()
    {
        var parsed = ReceiptStructuredOutputParser.ParseStructured(Receipt(
            items: """[{"name":"MASLO","quantity":1,"unit":null,"unitPrice":7.99,"lineTotal":7.99,"box":null}]""",
            discounts: """
                [{"label":"  ","amount":1.50,"appliesToItem":0,"box":null},
                 {"label":"Promocja","amount":-0.49,"appliesToItem":7,"box":null},
                 {"label":"X","amount":-0.10,"appliesToItem":0.5,"box":null}]
                """,
            total: 5.90m));

        var items = parsed.Extraction.Items;
        Assert.Equal(["MASLO", "RABAT", "Promocja", "X"], items.Select(item => item.Name));
        Assert.Equal([7.99m, -1.50m, -0.49m, -0.10m], items.Select(item => item.LineTotal!.Value));
        Assert.All(items.Skip(1), item =>
        {
            Assert.Equal(ReceiptLineKind.Discount, item.Kind);
            Assert.Null(item.Quantity);
            Assert.Null(item.UnitPrice);
        });
        // Discounts the model listed explicitly never trigger the "unmarked negative row" warning.
        Assert.Empty(parsed.Warnings);
    }

    [Fact]
    public void BoxesDegradeToNullAndOptionalBlocksMayBeMissing()
    {
        const string json = """
            {"merchantName":"LIDL","purchasedAt":null,"currency":"PLN","receiptTotal":3.00,
             "items":[
              {"name":"A","quantity":null,"unit":null,"unitPrice":null,"lineTotal":1.00,"box":{"x":0.9,"y":0.1,"width":0.5,"height":0.02}},
              {"name":"B","quantity":null,"unit":null,"unitPrice":null,"lineTotal":2.00,"box":"near the top"},
              {"name":"C","quantity":null,"unit":null,"unitPrice":null,"lineTotal":0.00}]}
            """;

        var extraction = ReceiptStructuredOutputParser.ParseStructured(json).Extraction;

        Assert.Equal(3, extraction.Items.Count);
        Assert.All(extraction.Items, item => Assert.Null(item.BoundingBox));
        Assert.Null(extraction.ReceiptTotalBoundingBox);
    }

    [Theory]
    [InlineData("""[{"label":"OPUST","amount":null,"appliesToItem":0,"box":null}]""", "$.discounts[0].amount is required.")]
    [InlineData("""[{"label":"OPUST","appliesToItem":0}]""", "$.discounts[0].amount is required.")]
    [InlineData("""[{"label":"OPUST","amount":-1,"note":"x"}]""", "$.discounts[0].note is not allowed.")]
    [InlineData("\"none\"", "$.discounts must be an array.")]
    public void InvalidDiscountsAreRejectedWithAPathForRepair(string discounts, string expectedError)
    {
        var exception = Assert.Throws<ReceiptStructuredOutputException>(() =>
            ReceiptStructuredOutputParser.ParseStructured(Receipt(
                items: """[{"name":"MASLO","quantity":1,"unit":null,"unitPrice":7.99,"lineTotal":7.99,"box":null}]""",
                discounts: discounts,
                total: 7.99m)));

        Assert.Contains(expectedError, exception.ValidationErrors);
    }

    [Fact]
    public void V3ShapeAndEmptyResultsAreRejected()
    {
        var v3 = Assert.Throws<ReceiptStructuredOutputException>(() => ReceiptStructuredOutputParser.ParseStructured(
            """{"rawLines":[],"merchantName":"X","purchasedAt":null,"currency":null,"receiptTotal":1,"receiptTotalBoundingBox":null,"items":[]}"""));
        Assert.Contains("$.rawLines is not allowed.", v3.ValidationErrors);

        var empty = Assert.Throws<ReceiptStructuredOutputException>(() => ReceiptStructuredOutputParser.ParseStructured(
            """{"merchantName":null,"purchasedAt":null,"currency":null,"items":[],"discounts":[],"subtotal":null,"taxTotal":null,"receiptTotal":null,"receiptTotalBox":{"x":0,"y":0,"width":0,"height":0}}"""));
        Assert.Contains(empty.ValidationErrors, error => error.Contains("empty", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WorkflowUsesTheRequestedContractAndRepairsWithIt()
    {
        var provider = new QueueProvider(["{not json", RecordedResponse]);
        var workflow = new ReceiptAnalysisWorkflow(new StructuredVisionProviderResolver([provider]));
        byte[] photo = [1, 2, 3];

        var result = await workflow.AnalyzeAsync(
            new ReceiptAnalysisRequest(
                Guid.NewGuid(),
                "correlation-v4",
                [new ReceiptImageData(photo, "image/jpeg"), new ReceiptImageData(photo, "image/jpeg")],
                provider.Id,
                ContractVersion: ReceiptAnalysisContractVersion.V4Structured),
            TestContext.Current.CancellationToken);

        Assert.Equal(6, result.Extraction.Items.Count);
        Assert.Equal(ReceiptStructuredAnalysisContract.PromptVersion, result.Metadata.PromptVersion);
        Assert.Equal(ReceiptStructuredAnalysisContract.SchemaVersion, result.Metadata.SchemaVersion);
        Assert.Equal(2, provider.Requests.Count);
        Assert.All(provider.Requests, request =>
        {
            Assert.Equal(ReceiptStructuredAnalysisContract.JsonSchema, request.JsonSchema);
            Assert.Equal(ReceiptStructuredAnalysisContract.PromptVersion, request.PromptVersion);
            Assert.Contains("kolejne części tego samego paragonu", request.Prompt);
        });
        Assert.Contains("not valid receipt JSON", provider.Requests[1].Prompt);
    }

    [Fact]
    public async Task WorkflowDefaultsToContractV3()
    {
        var provider = new QueueProvider([
            """{"merchantName":"X","purchasedAt":null,"currency":null,"receiptTotal":1,"items":[{"name":"A","quantity":1,"unit":null,"unitPrice":1,"lineTotal":1}]}"""
        ]);
        var workflow = new ReceiptAnalysisWorkflow(new StructuredVisionProviderResolver([provider]));

        var result = await workflow.AnalyzeAsync(
            new ReceiptAnalysisRequest(Guid.NewGuid(), "c", [new ReceiptImageData(new byte[] { 1 }, "image/jpeg")], provider.Id),
            TestContext.Current.CancellationToken);

        Assert.Equal(ReceiptAnalysisContract.PromptVersion, result.Metadata.PromptVersion);
        Assert.Equal(ReceiptAnalysisContract.JsonSchema, Assert.Single(provider.Requests).JsonSchema);
    }

    private static string Receipt(string items, string discounts, decimal total) => $$"""
        {"merchantName":"BIEDRONKA","purchasedAt":null,"currency":"PLN","items":{{items}},"discounts":{{discounts}},
         "subtotal":null,"taxTotal":null,"receiptTotal":{{total.ToString(System.Globalization.CultureInfo.InvariantCulture)}},"receiptTotalBox":null}
        """;

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PurchaseMemory.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Repository root was not found.");
    }

    [GeneratedRegex("FULL_PROMPT = \"\"\"\\\\\\n(?<text>.*?)\"\"\"", RegexOptions.Singleline)]
    private static partial Regex FullPromptLiteral();

    private sealed class QueueProvider(IEnumerable<string> responses) : IStructuredVisionProvider
    {
        private readonly Queue<string> _responses = new(responses);

        public string Id => "anthropic";

        public List<StructuredVisionRequest> Requests { get; } = [];

        public Task<StructuredVisionResponse> GenerateAsync(StructuredVisionRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new StructuredVisionResponse(_responses.Dequeue(), Id, "claude-haiku-5-5", "msg", "end_turn", 2_900, 2_500, 12_000));
        }

        public Task<StructuredVisionProviderStatus> GetStatusAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new StructuredVisionProviderStatus(true, Id, ["claude-haiku-5-5"], null));
    }
}
