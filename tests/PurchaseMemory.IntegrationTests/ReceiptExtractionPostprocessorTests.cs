using PurchaseMemory.Domain.Receipts;
using PurchaseMemory.Application.Receipts;

namespace PurchaseMemory.IntegrationTests;

public sealed class ReceiptExtractionPostprocessorTests
{
    [Theory]
    [InlineData("Biedronka 1234", "Biedronka")]
    [InlineData("JERONIMO MARTINS POLSKA S.A.", "Biedronka")]
    [InlineData("Lidl sp. z o.o.", "Lidl")]
    [InlineData("AUCHAN POLSKA", "Auchan")]
    [InlineData("Carrefour Express", "Carrefour")]
    public void CanonicalizesKnownMerchantVariants(string raw, string expected)
    {
        Assert.Equal(expected, ReceiptMerchantCanonicalizer.Canonicalize(raw));
    }

    [Fact]
    public void LeavesUnknownMerchantNameUnchanged()
    {
        Assert.Equal("Local Corner Shop", ReceiptMerchantCanonicalizer.Canonicalize("Local Corner Shop"));
    }

    [Fact]
    public void LeavesNullOrEmptyMerchantNameUnchanged()
    {
        Assert.Null(ReceiptMerchantCanonicalizer.Canonicalize(null));
        Assert.Equal("", ReceiptMerchantCanonicalizer.Canonicalize(""));
    }

    [Theory]
    [InlineData("OPUST -2,50")]
    [InlineData("RABAT KARTA STALEGO KLIENTA")]
    [InlineData("promocja -5%")]
    public void DetectsDiscountLines(string name)
    {
        Assert.True(ReceiptDiscountLineFilter.IsDiscountLine(name));
    }

    [Theory]
    [InlineData("Mleko 3.2% 1L")]
    [InlineData("Chleb pszenny")]
    public void DoesNotFlagRegularItemsAsDiscountLines(string name)
    {
        Assert.False(ReceiptDiscountLineFilter.IsDiscountLine(name));
    }

    [Fact]
    public void KeepsDiscountLinesInPlaceAndMarksThemAsDiscounts()
    {
        var items = new List<ReceiptExtractionItem>
        {
            new("Mleko", 1m, "szt", 4.5m, 4.5m),
            new("OPUST -2,50", null, null, null, -2.5m),
            new("Chleb", 1m, "szt", 3.0m, 3.0m),
        };

        var result = ReceiptDiscountLineFilter.ClassifyDiscountLines(items);

        Assert.Equal(["Mleko", "OPUST -2,50", "Chleb"], result.Select(item => item.Name));
        Assert.Equal(
            [ReceiptLineKind.Product, ReceiptLineKind.Discount, ReceiptLineKind.Product],
            result.Select(item => item.Kind));
        Assert.Equal(-2.5m, result[1].LineTotal);
    }

    [Fact]
    public void StoresAPositiveDiscountAmountAsNegative()
    {
        var result = ReceiptDiscountLineFilter.ClassifyDiscountLines(
            [new("Mleko", 1m, "szt", 10m, 10m), new("RABAT", null, null, null, 3m)]);

        Assert.Equal(ReceiptLineKind.Discount, result[1].Kind);
        Assert.Equal(-3m, result[1].LineTotal);
    }

    [Fact]
    public void ProductWithMarkerWordAndUnitPriceStaysAProduct()
    {
        var result = ReceiptDiscountLineFilter.ClassifyDiscountLines(
            [new("Chipsy PROMOCJA", 1m, "szt", 3.99m, 3.99m)]);

        Assert.Equal(ReceiptLineKind.Product, Assert.Single(result).Kind);
        Assert.Equal(3.99m, result[0].LineTotal);
    }

    [Fact]
    public void TreatsAnyNegativeLineTotalAsDiscountLine()
    {
        Assert.True(ReceiptDiscountLineFilter.IsDiscountLine(new ReceiptExtractionItem("OPUS A ES A ANE-", null, null, null, -4.38m)));
        Assert.True(ReceiptDiscountLineFilter.IsDiscountLine(new ReceiptExtractionItem("RABAT", null, null, null, null)));
        Assert.False(ReceiptDiscountLineFilter.IsDiscountLine(new ReceiptExtractionItem("Mleko", 1m, "szt", 4.5m, 4.5m)));
        Assert.False(ReceiptDiscountLineFilter.IsDiscountLine(new ReceiptExtractionItem("Mleko", null, null, null, null)));
    }

    [Fact]
    public void ApplyCanonicalizesMerchantAndKeepsDiscountLines()
    {
        var extraction = new ReceiptExtractionResult(
            "biedronka 1234",
            null,
            "PLN",
            7.5m,
            new List<ReceiptExtractionItem>
            {
                new("Mleko", 1m, "szt", 4.5m, 4.5m),
                new("RABAT", null, null, null, -1m),
                new("Chleb", 1m, "szt", 3.0m, 3.0m),
            },
            "ollama",
            "qwen3-vl:8b-instruct");

        var result = ReceiptExtractionPostprocessor.Apply(extraction);

        Assert.Equal("Biedronka", result.MerchantName);
        Assert.Equal(3, result.Items.Count);
        Assert.Equal(ReceiptLineKind.Discount, result.Items[1].Kind);
        Assert.Equal(6.5m, result.Items.Sum(item => item.LineTotal));
    }

    [Fact]
    public void ApplyIsANoOpWhenNothingToClean()
    {
        var extraction = new ReceiptExtractionResult(
            "Unknown Shop",
            null,
            "PLN",
            4.5m,
            new List<ReceiptExtractionItem> { new("Mleko", 1m, "szt", 4.5m, 4.5m) },
            "ollama",
            "qwen3-vl:8b-instruct");

        var result = ReceiptExtractionPostprocessor.Apply(extraction);

        Assert.Equal("Unknown Shop", result.MerchantName);
        Assert.Single(result.Items);
    }
}
