using System.Text.Json.Nodes;
using PurchaseMemory.Application.Receipts;
using PurchaseMemory.Domain.Receipts;

namespace PurchaseMemory.IntegrationTests;

public sealed class ReceiptStructuredOutputParserTests
{
    [Fact]
    public void RuntimeSchemaMatchesNormativeDocument()
    {
        var documented = JsonNode.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "receipt-extract-result-v3.schema.json")));
        var runtime = JsonNode.Parse(ReceiptAnalysisContract.JsonSchema);

        Assert.True(JsonNode.DeepEquals(documented, runtime));
    }

    [Fact]
    public void AcceptsTimestampWithNumericOffsetAndNoSeconds()
    {
        var parsed = ReceiptStructuredOutputParser.Parse(ValidJson("2026-07-31T18:42+02:00"));

        Assert.Equal(
            new DateTimeOffset(2026, 7, 31, 18, 42, 0, TimeSpan.FromHours(2)),
            parsed.Extraction.PurchasedAt);
    }

    [Theory]
    [InlineData("07/31/2026 18:42 +02:00")]
    [InlineData("31.07.2026 18:42 +02:00")]
    [InlineData("2026-07-31 18:42+02:00")]
    public void RejectsNonIsoTimestampEvenWithNumericOffset(string timestamp)
    {
        Assert.Throws<ReceiptStructuredOutputException>(
            () => ReceiptStructuredOutputParser.Parse(ValidJson(timestamp)));
    }

    [Theory]
    [InlineData("""{"merchantName":null,"purchasedAt":null,"currency":null,"receiptTotal":null,"items":[],"extra":1}""")]
    [InlineData("""{"merchantName":null,"purchasedAt":null,"currency":null,"receiptTotal":null}""")]
    [InlineData("""{"merchantName":null,"purchasedAt":null,"currency":null,"receiptTotal":null,"items":"wrong"}""")]
    [InlineData("""{"merchantName":null,"purchasedAt":null,"currency":null,"receiptTotal":null,"items":[{"name":4,"quantity":1,"unit":null,"unitPrice":1,"lineTotal":1}]}""")]
    public void RejectsSchemaViolationsAsRepairableError(string json)
    {
        Assert.Throws<ReceiptStructuredOutputException>(() => ReceiptStructuredOutputParser.Parse(json));
    }

    [Fact]
    public void RejectsAllEmptyResultAsRepairableErrorInsteadOfSilentSuccess()
    {
        // Track B regression: qwen3-vl:8b-instruct occasionally returned this
        // exact schema-valid but entirely empty shape (no merchant, no total,
        // no items) instead of throwing or returning malformed JSON. A model
        // regression like this must fail the same way malformed JSON does, so
        // the router falls back to the next configured pipeline instead of
        // silently saving an empty purchase.
        const string emptyJson = """
            {"merchantName":null,"purchasedAt":null,"currency":null,"receiptTotal":null,"items":[]}
            """;

        var exception = Assert.Throws<ReceiptStructuredOutputException>(
            () => ReceiptStructuredOutputParser.Parse(emptyJson));

        Assert.Contains(exception.ValidationErrors, error => error.Contains("empty", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AcceptsPartialResultWithOnlyAMerchantNameAsNotEmpty()
    {
        // Only fully empty results are rejected: a receipt with just a
        // merchant name (or just a total, or just one item) is a legitimate,
        // if incomplete, draft and must still reach manual review.
        const string merchantOnlyJson = """
            {"merchantName":"BIEDRONKA","purchasedAt":null,"currency":null,"receiptTotal":null,"items":[]}
            """;

        var parsed = ReceiptStructuredOutputParser.Parse(merchantOnlyJson);

        Assert.Equal("BIEDRONKA", parsed.Extraction.MerchantName);
    }

    [Fact]
    public void ProducesArithmeticWarningsWithoutRejectingDraft()
    {
        const string json = """
            {
              "merchantName": null,
              "purchasedAt": null,
              "currency": "PLN",
              "receiptTotal": 10,
              "items": [
                {"name":"A","quantity":2,"unit":"szt","unitPrice":2,"lineTotal":5}
              ]
            }
            """;

        var parsed = ReceiptStructuredOutputParser.Parse(json);

        Assert.Equal(2, parsed.Warnings.Count);
    }

    [Theory]
    [InlineData("Rabat")]
    [InlineData("OPUST -2,50")]
    [InlineData("promocja -5%")]
    public void AcceptsNegativeLineTotalOnRecognizedDiscountMarkerRow(string discountName)
    {
        // qwen3-vl:8b-instruct sometimes includes a discount/promo row (e.g.
        // "Rabat", lineTotal -2.0) in the items array despite the prompt
        // instructing it not to. ReceiptExtractionPostprocessor marks these rows
        // as discount lines by the same name-marker match, but only runs after
        // Parse succeeds — so a negative lineTotal on one of these recognized
        // rows must not fail validation here.
        var json = ItemsJson([(discountName, -2.0m)]);

        var parsed = ReceiptStructuredOutputParser.Parse(json);

        var item = Assert.Single(parsed.Extraction.Items);
        Assert.Equal(discountName, item.Name);
        Assert.Equal(-2.0m, item.LineTotal);
    }

    [Fact]
    public void AcceptsNegativeLineTotalWithoutDiscountMarkerAndFlagsItForReview()
    {
        // OCR garbled "OPUST" on a real receipt; the row must not reject the
        // whole answer, but it must be flagged because nothing in its name
        // confirms it is a discount.
        var json = ItemsJson([("Mleko", 7.5m), ("OPUS A ES A ANE- 1 D2", -2.0m)]);

        var parsed = ReceiptStructuredOutputParser.Parse(json);

        Assert.Equal(2, parsed.Extraction.Items.Count);
        var warning = Assert.Single(parsed.Warnings);
        Assert.Contains("Item 2 has a negative total but no discount marker", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void MarkedDiscountRowIsNotFlaggedForReview()
    {
        var parsed = ReceiptStructuredOutputParser.Parse(ItemsJson([("Mleko", 7.5m), ("OPUST", -2.0m)]));

        Assert.Empty(parsed.Warnings);
    }

    [Fact]
    public void AcceptsUnmarkedNegativeRowAsTheOnlyItemWithWarnings()
    {
        var parsed = ReceiptStructuredOutputParser.Parse(ItemsJson([("XYZ", -2.0m)]));

        Assert.Single(parsed.Extraction.Items);
        Assert.Contains(parsed.Warnings, warning => warning.Contains("needs review", StringComparison.Ordinal));
        Assert.Contains(parsed.Warnings, warning => warning.Contains("Receipt total does not match", StringComparison.Ordinal));
        Assert.Equal(ReceiptLineKind.Discount, Assert.Single(ReceiptExtractionPostprocessor.Apply(parsed.Extraction).Items).Kind);
    }

    [Fact]
    public void AcceptsUnmarkedNegativeRowLargerThanProductsWithWarnings()
    {
        var parsed = ReceiptStructuredOutputParser.Parse(ItemsJson([("Mleko", 3.0m), ("XYZ", -9.0m)]));

        Assert.Equal(2, parsed.Warnings.Count);
        Assert.Contains(parsed.Warnings, warning => warning.Contains("needs review", StringComparison.Ordinal));
        Assert.Contains(parsed.Warnings, warning => warning.Contains("Receipt total does not match", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("""{"name":"Mleko","quantity":-1,"unit":null,"unitPrice":null,"lineTotal":-2}""", "quantity must be greater than zero")]
    [InlineData("""{"name":"Mleko","quantity":null,"unit":null,"unitPrice":-2,"lineTotal":-2}""", "unitPrice cannot be negative")]
    [InlineData("""{"name":"","quantity":null,"unit":null,"unitPrice":null,"lineTotal":-2}""", "name must contain")]
    public void NegativeLineTotalDoesNotRelaxOtherItemChecks(string item, string expectedError)
    {
        var json = $$"""{"merchantName":"Biedronka","purchasedAt":null,"currency":"PLN","receiptTotal":5.5,"items":[{{item}}]}""";

        var exception = Assert.Throws<ReceiptStructuredOutputException>(
            () => ReceiptStructuredOutputParser.Parse(json));

        Assert.Contains(exception.ValidationErrors, error => error.Contains(expectedError, StringComparison.Ordinal));
    }

    [Fact]
    public void UnmarkedNegativeRowIsKeptAsADiscountLikeAMarkedOne()
    {
        var json = ItemsJson([("Mleko", 4.5m), ("OPUS A ES A ANE-", -2.0m), ("Rabat", -1.0m), ("Chleb", 4.0m)]);

        var cleaned = ReceiptExtractionPostprocessor.Apply(ReceiptStructuredOutputParser.Parse(json).Extraction);

        Assert.Equal(["Mleko", "OPUS A ES A ANE-", "Rabat", "Chleb"], cleaned.Items.Select(item => item.Name));
        Assert.Equal(
            [ReceiptLineKind.Product, ReceiptLineKind.Discount, ReceiptLineKind.Discount, ReceiptLineKind.Product],
            cleaned.Items.Select(item => item.Kind));
    }

    [Fact]
    public void DiscountRowSurvivesValidationThenIsKeptAsDiscountEndToEnd()
    {
        // The discount row must pass Parse (no ReceiptStructuredOutputException
        // and no fallback to another pipeline) and then be kept by the
        // postprocessor as a discount line in its printed place.
        var json = ItemsJson(
            [
                ("Mleko", 4.5m),
                ("Rabat", -2.0m),
                ("Chleb", 3.0m),
            ]);

        var parsed = ReceiptStructuredOutputParser.Parse(json);
        var cleaned = ReceiptExtractionPostprocessor.Apply(parsed.Extraction);

        Assert.Equal(3, cleaned.Items.Count);
        Assert.Equal(ReceiptLineKind.Discount, cleaned.Items.Single(item => item.Name == "Rabat").Kind);
        Assert.DoesNotContain(parsed.Warnings, warning => warning.Contains("Receipt total does not match", StringComparison.Ordinal));
    }

    [Fact]
    public void AcceptsRawLinesAndDropsThemFromTheExtraction()
    {
        var json = """
            {"rawLines":["Mleko 2% X","1 x4,49 4,49A","OPUST Mleko 2%","-1,00"],
             "merchantName":"Lidl","purchasedAt":null,"currency":"PLN","receiptTotal":3.49,
             "receiptTotalBoundingBox":null,
             "items":[
               {"name":"Mleko 2%","quantity":1,"unit":null,"unitPrice":4.49,"lineTotal":4.49,"boundingBox":null},
               {"name":"OPUST Mleko 2%","quantity":null,"unit":null,"unitPrice":null,"lineTotal":-1.00,"boundingBox":null}
             ]}
            """;

        var parsed = ReceiptStructuredOutputParser.Parse(json);

        Assert.Equal(2, parsed.Extraction.Items.Count);
        Assert.Empty(parsed.Warnings);
        Assert.DoesNotContain(
            typeof(ReceiptExtractionResult).GetProperties(),
            property => property.Name.Contains("Raw", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ResultWithoutRawLinesStillParses()
    {
        Assert.Single(ReceiptStructuredOutputParser.Parse(ValidJson("2026-07-31T18:42+02:00")).Extraction.Items);
    }

    [Fact]
    public void PromptAsksForDiscountRowsAndRawLines()
    {
        var prompt = ReceiptAnalysisContract.BuildPrompt(null);

        Assert.Contains("rawLines", prompt, StringComparison.Ordinal);
        Assert.Contains("UJEMNYM lineTotal", prompt, StringComparison.Ordinal);
        Assert.Equal("receipt.extract.prompt.v3", ReceiptAnalysisContract.PromptVersion);
    }

    private static string ItemsJson(IReadOnlyList<(string Name, decimal LineTotal)> items)
    {
        var itemsJson = string.Join(",", items.Select(item =>
            $$"""{"name":"{{item.Name}}","quantity":null,"unit":null,"unitPrice":null,"lineTotal":{{item.LineTotal.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}"""));
        return $$"""
            {"merchantName":"Biedronka","purchasedAt":null,"currency":"PLN","receiptTotal":5.5,"items":[{{itemsJson}}]}
            """;
    }

    private static string ValidJson(string timestamp) => $$"""
        {
          "merchantName": "Lidl",
          "purchasedAt": "{{timestamp}}",
          "currency": "PLN",
          "receiptTotal": 1,
          "items": [
            {"name":"MLEKO","quantity":1,"unit":"szt","unitPrice":1,"lineTotal":1}
          ]
        }
        """;
}
