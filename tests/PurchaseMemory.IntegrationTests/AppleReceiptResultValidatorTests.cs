using PurchaseMemory.Application.Receipts;
using PurchaseMemory.Infrastructure.Recognition;

namespace PurchaseMemory.IntegrationTests;

public sealed class AppleReceiptResultValidatorTests
{
    [Fact]
    public void AppliesProductDiscountAndPassesArithmeticValidation()
    {
        var receipt = CreateReceipt(
            8.27m,
            new AppleSidecarReceiptItem
            {
                Name = "ChlebPszen-Zyt550g",
                Quantity = 1,
                Unit = "pcs",
                UnitPrice = 1.99m,
                GrossLineTotal = 1.99m,
                Discount = 0,
                LineTotal = 1.99m
            },
            new AppleSidecarReceiptItem
            {
                Name = "Ser ż.Swiat. 150g",
                Quantity = 1,
                UnitPrice = 3.29m,
                GrossLineTotal = 3.29m,
                LineTotal = 3.29m
            },
            new AppleSidecarReceiptItem
            {
                Name = "Polędu kos 100g",
                Quantity = 1,
                UnitPrice = 4.99m,
                GrossLineTotal = 4.99m,
                Discount = 2.00m,
                LineTotal = 2.99m
            });

        var result = AppleReceiptResultValidator.ValidateAndCreate(receipt);

        Assert.True(result.ArithmeticMatches);
        Assert.Equal(8.27m, result.LineTotalSum);
        Assert.Equal(2.99m, result.Extraction.Items[2].LineTotal);
        Assert.Equal(TimeSpan.FromHours(1), result.Extraction.PurchasedAt?.Offset);
    }

    [Fact]
    public void RepairsMissingDecimalSeparatorBeforeValidation()
    {
        var receipt = CreateReceipt(
            102.62m,
            new AppleSidecarReceiptItem
            {
                Name = "Filet piersi kurczaka",
                Quantity = 1,
                UnitPrice = 18_460m,
                GrossLineTotal = 18_460m,
                Discount = 6.06m,
                LineTotal = 18_460m
            },
            new AppleSidecarReceiptItem
            {
                Name = "Pozostałe produkty",
                LineTotal = 84.16m
            });

        var result = AppleReceiptResultValidator.ValidateAndCreate(receipt);

        Assert.True(result.ArithmeticMatches);
        Assert.True(result.CorrectedMoneyValues > 0);
        Assert.Equal(18.46m, result.Extraction.Items[0].LineTotal);
        Assert.Equal(102.62m, result.LineTotalSum);
    }

    [Fact]
    public void KeepsPlausibleIncompleteResultForManualReview()
    {
        var receipt = CreateReceipt(
            100m,
            new AppleSidecarReceiptItem { Name = "Produkt A", LineTotal = 45m },
            new AppleSidecarReceiptItem { Name = "Produkt B", LineTotal = 40m });

        var result = AppleReceiptResultValidator.ValidateAndCreate(receipt);

        Assert.False(result.ArithmeticMatches);
        Assert.Equal(85m, result.LineTotalSum);
    }

    [Fact]
    public void RejectsSeverelyBrokenArithmeticSoRouterCanFallback()
    {
        var receipt = CreateReceipt(
            100m,
            new AppleSidecarReceiptItem { Name = "Produkt A", LineTotal = 10m });

        var exception = Assert.Throws<ReceiptExtractionException>(
            () => AppleReceiptResultValidator.ValidateAndCreate(receipt));

        Assert.Contains("50 percent", exception.Message, StringComparison.Ordinal);
    }

    private static AppleSidecarReceipt CreateReceipt(
        decimal total,
        params AppleSidecarReceiptItem[] items)
    {
        return new AppleSidecarReceipt
        {
            MerchantName = "Biedronka",
            PurchasedAt = "2020-01-09 13:18",
            Currency = "PLN",
            ReceiptTotal = total,
            Items = items
        };
    }
}
