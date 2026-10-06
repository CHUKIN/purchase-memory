using Microsoft.Extensions.DependencyInjection;
using PurchaseMemory.Application.Purchases;

namespace PurchaseMemory.IntegrationTests;

public sealed class SourceReceiptPurchaseTests
{
    [Fact]
    public async Task RepositoryFindsPurchaseBySourceAndServiceCreationIsIdempotent()
    {
        await using var application = await TestApplication.CreateAsync();
        var sourceReceiptId = Guid.NewGuid();
        Guid firstPurchaseId;

        await using (var createScope = application.Services.CreateAsyncScope())
        {
            var service = createScope.ServiceProvider.GetRequiredService<IPurchaseDraftService>();
            var first = await service.CreateAsync(
                CreateCommand(sourceReceiptId, "SOURCE ITEM"),
                TestContext.Current.CancellationToken);
            var repeated = await service.CreateAsync(
                CreateCommand(sourceReceiptId, "CHANGED ITEM MUST NOT REPLACE SOURCE"),
                TestContext.Current.CancellationToken);

            Assert.Equal(first.PurchaseId, repeated.PurchaseId);
            Assert.Equal("SOURCE ITEM", Assert.Single(repeated.Items).Name);
            firstPurchaseId = first.PurchaseId;
        }

        await using var verificationScope = application.Services.CreateAsyncScope();
        var repository = verificationScope.ServiceProvider.GetRequiredService<IPurchaseDraftRepository>();
        var stored = await repository.FindBySourceReceiptIdAsync(
            sourceReceiptId,
            TestContext.Current.CancellationToken);

        Assert.NotNull(stored);
        Assert.Equal(firstPurchaseId, stored.Id);
        Assert.Equal(sourceReceiptId, stored.SourceReceiptId);
        Assert.Equal("SOURCE ITEM", Assert.Single(stored.Items).Name);
    }

    private static CreatePurchaseDraftCommand CreateCommand(Guid sourceReceiptId, string itemName)
    {
        return new CreatePurchaseDraftCommand(
            "Source test",
            new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.FromHours(2)),
            "PLN",
            3.50m,
            [new PurchaseItemCommand(itemName, 1, "szt", 3.50m, 3.50m)],
            SourceReceiptId: sourceReceiptId);
    }
}
