using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using PurchaseMemory.Api.Mcp;
using PurchaseMemory.Application.Purchases;

namespace PurchaseMemory.IntegrationTests;

public sealed class PurchaseToolsErrorTests
{
    [Fact]
    public async Task CreateToolExposesSafeValidationMessage()
    {
        await using var application = await TestApplication.CreateAsync();
        await using var scope = application.Services.CreateAsyncScope();
        var tools = CreateTools(scope.ServiceProvider);

        var exception = await Assert.ThrowsAsync<McpException>(() =>
            tools.CreatePurchaseDraftAsync(
                new CreatePurchaseDraftRequest { Items = [] },
                TestContext.Current.CancellationToken));

        Assert.Contains("at least one visible item", exception.Message);
    }

    [Fact]
    public async Task UpdateToolExposesSafeNotFoundMessage()
    {
        await using var application = await TestApplication.CreateAsync();
        await using var scope = application.Services.CreateAsyncScope();
        var tools = CreateTools(scope.ServiceProvider);
        var missingPurchaseId = Guid.Parse("00000000-0000-0000-0000-000000000001");

        var exception = await Assert.ThrowsAsync<McpException>(() =>
            tools.UpdatePurchaseDraftAsync(
                new UpdatePurchaseDraftRequest
                {
                    PurchaseId = missingPurchaseId,
                    Items = [new PurchaseItemRequest { Name = "ITEM" }]
                },
                TestContext.Current.CancellationToken));

        Assert.Contains(missingPurchaseId.ToString(), exception.Message);
        Assert.Contains("was not found", exception.Message);
    }

    [Fact]
    public async Task SearchToolExposesSafeValidationMessage()
    {
        await using var application = await TestApplication.CreateAsync();
        await using var scope = application.Services.CreateAsyncScope();
        var tools = CreateTools(scope.ServiceProvider);

        var exception = await Assert.ThrowsAsync<McpException>(() =>
            tools.SearchPurchasesAsync(
                new string('a', 201),
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("cannot exceed 200 characters", exception.Message);
    }

    [Fact]
    public async Task CreateToolExposesExistingPurchaseIdForDuplicate()
    {
        await using var application = await TestApplication.CreateAsync();
        await using var scope = application.Services.CreateAsyncScope();
        var tools = CreateTools(scope.ServiceProvider);
        var request = new CreatePurchaseDraftRequest
        {
            MerchantName = "Tool duplicate",
            PurchasedAt = new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.FromHours(2)),
            Currency = "PLN",
            ReceiptTotal = 2.50m,
            Items =
            [
                new PurchaseItemRequest
                {
                    Name = "SAME TOOL ITEM",
                    Quantity = 1,
                    Unit = "pcs",
                    UnitPrice = 2.50m,
                    LineTotal = 2.50m
                }
            ]
        };

        var first = await tools.CreatePurchaseDraftAsync(
            request,
            TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<McpException>(() =>
            tools.CreatePurchaseDraftAsync(request, TestContext.Current.CancellationToken));

        Assert.Contains(first.PurchaseId.ToString(), exception.Message);
        Assert.Contains("allowPossibleDuplicate", exception.Message);
    }

    private static PurchaseTools CreateTools(IServiceProvider serviceProvider)
    {
        return new PurchaseTools(
            serviceProvider.GetRequiredService<IPurchaseDraftService>(),
            NullLogger<PurchaseTools>.Instance);
    }
}
