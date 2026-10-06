using System.Text.Json;
using PurchaseMemory.Api.Endpoints;
using PurchaseMemory.Application.Purchases;
using PurchaseMemory.Domain.Receipts;

namespace PurchaseMemory.IntegrationTests;

public sealed class ReceiptLineKindWireTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public void PurchaseResponseSendsKindAsTheLowercaseStringThePwaExpects()
    {
        var now = new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);
        var view = new PurchaseDraftView(
            Guid.NewGuid(),
            "Lidl",
            now,
            "PLN",
            69.99m,
            now,
            now,
            [
                new PurchaseItemView("Odkurzacz", 1, null, 99.99m, 99.99m),
                new PurchaseItemView("OPUST", null, null, null, -30m, ReceiptLineKind.Discount, 0)
            ],
            [],
            null);

        var json = JsonSerializer.Serialize(PurchaseResponse.FromView(view), WebJson);

        Assert.Contains("\"kind\":\"product\",\"discountedItemPosition\":null", json, StringComparison.Ordinal);
        Assert.Contains("\"kind\":\"discount\",\"discountedItemPosition\":0", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ReviewRequestReadsKindAndIgnoresTheClientsDiscountLink()
    {
        var request = new ReviewReceiptRequest(
            "Lidl",
            null,
            "PLN",
            69.99m,
            [
                new ReviewReceiptItemRequest("Odkurzacz", 1, null, 99.99m, 99.99m, Kind: "product"),
                new ReviewReceiptItemRequest("OPUST", null, null, null, -30m, Kind: "Discount", DiscountedItemPosition: 7),
                new ReviewReceiptItemRequest("Mleko", 1, null, 4m, 4m)
            ]);

        var command = request.ToCommand(Guid.NewGuid());

        Assert.Equal([ReceiptLineKind.Product, ReceiptLineKind.Discount, null], command.Items.Select(item => item.Kind));
    }

    [Fact]
    public void AnUnknownKindIsABadRequest()
    {
        var request = new ReviewReceiptRequest(
            null, null, null, null, [new ReviewReceiptItemRequest("X", null, null, null, 1m, Kind: "coupon")]);

        Assert.Throws<ArgumentException>(() => request.ToCommand(Guid.NewGuid()));
    }
}
