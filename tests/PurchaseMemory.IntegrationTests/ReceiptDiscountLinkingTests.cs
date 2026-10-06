using PurchaseMemory.Domain.Purchases;
using PurchaseMemory.Domain.Receipts;

namespace PurchaseMemory.IntegrationTests;

public sealed class ReceiptDiscountLinkingTests
{
    [Fact]
    public void EachDiscountBelongsToTheNearestProductAboveIt()
    {
        var links = ReceiptDiscountLinking.Resolve(
        [
            Discount("OPUST", -1m),
            Product("Mleko", 5m),
            Discount("OPUST", -1m),
            Discount("RABAT", -0.5m),
            Product("Chleb", 3m),
            Product("Kawa", 20m),
            Discount("OPUST", -2m)
        ]);

        Assert.Equal([null, null, 1, 1, null, null, 5], links.Select(link => link.TargetPosition));
        Assert.All(links, link => Assert.False(link.AppliesToReceipt));
    }

    [Theory]
    [InlineData("OPUST (podsuma)", -2)]
    [InlineData("RABAT NA PARAGON", -2)]
    [InlineData("Rabat na całe zakupy", -2)]
    [InlineData("OPUST", -21.59)]
    public void AReceiptLevelDiscountIsNotAttachedToTheProductAboveIt(string name, double amount)
    {
        var links = ReceiptDiscountLinking.Resolve(
            [Product("Pudełko na pizzę", 5m), Discount("OPUST", -2m), Discount(name, (decimal)amount)]);

        Assert.Equal(new ReceiptDiscountLink(0, false), links[1]);
        Assert.Equal(new ReceiptDiscountLink(null, true), links[2]);
    }

    [Fact]
    public void ADiscountEqualToItsProductStillBelongsToIt()
    {
        var links = ReceiptDiscountLinking.Resolve([Product("Gratis", 2m), Discount("OPUST", -2m)]);

        Assert.Equal(new ReceiptDiscountLink(0, false), links[1]);
    }

    private static ReceiptLineForLinking Product(string name, decimal total) => new(ReceiptLineKind.Product, name, total);

    private static ReceiptLineForLinking Discount(string name, decimal total) => new(ReceiptLineKind.Discount, name, total);

    [Fact]
    public void ADiscountLineCannotHaveAPositiveTotal()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ReceiptCaptureItem.Create(
            1, "OPUST", null, null, null, 2m, kind: ReceiptLineKind.Discount, discountedItemPosition: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => PurchaseItem.Create(
            1, "OPUST", null, null, null, 2m, ReceiptLineKind.Discount, 0));
    }

    [Fact]
    public void AProductLineStillRejectsANegativeTotalAndADiscountLink()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ReceiptCaptureItem.Create(
            0, "Mleko", 1, null, 4.49m, -4.49m));
        Assert.Throws<ArgumentOutOfRangeException>(() => PurchaseItem.Create(
            0, "Mleko", 1, null, 4.49m, -4.49m, ReceiptLineKind.Product));
        Assert.Throws<ArgumentException>(() => ReceiptCaptureItem.Create(
            1, "Mleko", 1, null, 4.49m, 4.49m, discountedItemPosition: 0));
    }

    [Fact]
    public void ADiscountCanOnlyReferToALineAboveIt()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ReceiptCaptureItem.Create(
            1, "OPUST", null, null, null, -1m, kind: ReceiptLineKind.Discount, discountedItemPosition: 1));

        var discount = ReceiptCaptureItem.Create(
            1, "OPUST", null, null, null, -1m, kind: ReceiptLineKind.Discount, discountedItemPosition: 0);
        Assert.Equal(0, discount.DiscountedItemPosition);
    }
}
