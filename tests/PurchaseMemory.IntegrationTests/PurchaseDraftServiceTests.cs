using Microsoft.Extensions.DependencyInjection;
using PurchaseMemory.Application.Purchases;

namespace PurchaseMemory.IntegrationTests;

public sealed class PurchaseDraftServiceTests
{
    [Fact]
    public async Task CreateAsyncPersistsDraftAndItems()
    {
        await using var application = await TestApplication.CreateAsync();
        await using var scope = application.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IPurchaseDraftService>();

        var created = await service.CreateAsync(
            new CreatePurchaseDraftCommand(
                "Biedronka",
                new DateTimeOffset(2026, 7, 10, 18, 30, 0, TimeSpan.FromHours(2)),
                "pln",
                12.48m,
                [
                    new PurchaseItemCommand("MLEKO 3,2%", 1, "pcs", 4.99m, 4.99m),
                    new PurchaseItemCommand("CHLEB ŻYTNI", 1, "pcs", 7.49m, 7.49m)
                ]),
            TestContext.Current.CancellationToken);

        Assert.NotEqual(Guid.Empty, created.PurchaseId);
        Assert.Equal("Biedronka", created.MerchantName);
        Assert.Equal("PLN", created.Currency);
        Assert.Equal(12.48m, created.ReceiptTotal);
        Assert.Collection(
            created.Items,
            item => Assert.Equal("MLEKO 3,2%", item.Name),
            item => Assert.Equal("CHLEB ŻYTNI", item.Name));
        Assert.Empty(created.Issues);
    }

    [Fact]
    public async Task UpdateAsyncReplacesPersistedSnapshot()
    {
        await using var application = await TestApplication.CreateAsync();
        Guid purchaseId;

        await using (var createScope = application.Services.CreateAsyncScope())
        {
            var service = createScope.ServiceProvider.GetRequiredService<IPurchaseDraftService>();
            var created = await service.CreateAsync(
                new CreatePurchaseDraftCommand(
                    "Lidl",
                    null,
                    "PLN",
                    8.50m,
                    [new PurchaseItemCommand("KAWA", 1, "pcs", 8.50m, 8.50m)]),
                TestContext.Current.CancellationToken);

            purchaseId = created.PurchaseId;
        }

        await using (var updateScope = application.Services.CreateAsyncScope())
        {
            var service = updateScope.ServiceProvider.GetRequiredService<IPurchaseDraftService>();
            var updated = await service.UpdateAsync(
                new UpdatePurchaseDraftCommand(
                    purchaseId,
                    "Lidl Polska",
                    null,
                    "PLN",
                    9.50m,
                    [new PurchaseItemCommand("KAWA ZIARNISTA", 1, "pcs", 9.50m, 9.50m)]),
                TestContext.Current.CancellationToken);

            Assert.Equal("Lidl Polska", updated.MerchantName);
            Assert.Equal(9.50m, updated.ReceiptTotal);
            Assert.Equal("KAWA ZIARNISTA", Assert.Single(updated.Items).Name);
        }

        await using var verificationScope = application.Services.CreateAsyncScope();
        var verificationService = verificationScope.ServiceProvider.GetRequiredService<IPurchaseDraftService>();
        var matches = await verificationService.SearchAsync(
            "ziarnista",
            TestContext.Current.CancellationToken);

        Assert.Equal(purchaseId, Assert.Single(matches).PurchaseId);
    }

    [Fact]
    public async Task SearchAsyncFindsUnicodeTextIgnoringCase()
    {
        await using var application = await TestApplication.CreateAsync();

        await using (var createScope = application.Services.CreateAsyncScope())
        {
            var service = createScope.ServiceProvider.GetRequiredService<IPurchaseDraftService>();

            await service.CreateAsync(
                new CreatePurchaseDraftCommand(
                    "Русский магазин",
                    null,
                    "PLN",
                    5.25m,
                    [new PurchaseItemCommand("Сыр сливочный", 1, "шт", 5.25m, 5.25m)]),
                TestContext.Current.CancellationToken);

            await service.CreateAsync(
                new CreatePurchaseDraftCommand(
                    "Żabka",
                    null,
                    "PLN",
                    3.20m,
                    [new PurchaseItemCommand("WODA", 1, "pcs", 3.20m, 3.20m)]),
                TestContext.Current.CancellationToken);
        }

        await using var searchScope = application.Services.CreateAsyncScope();
        var searchService = searchScope.ServiceProvider.GetRequiredService<IPurchaseDraftService>();

        var itemMatches = await searchService.SearchAsync(
            "СЫР",
            TestContext.Current.CancellationToken);
        var merchantMatches = await searchService.SearchAsync(
            "żABKA",
            TestContext.Current.CancellationToken);

        Assert.Equal("Русский магазин", Assert.Single(itemMatches).MerchantName);
        Assert.Equal("Żabka", Assert.Single(merchantMatches).MerchantName);
    }

    [Fact]
    public async Task CreateAsyncReportsReceiptTotalMismatchAsAReceiptLevelIssue()
    {
        var created = await CreateDraftAsync(
            10m,
            [
                new PurchaseItemCommand("ITEM A", 1, null, 4m, 4m),
                new PurchaseItemCommand("ITEM B", 1, null, 5m, 5m)
            ]);

        var issue = Assert.Single(created.Issues);
        Assert.Equal(PurchaseDraftIssueType.ReceiptTotalMismatch, issue.Type);
        Assert.Null(issue.ItemPosition);
        Assert.Null(issue.ItemName);
        Assert.Equal("9.00 PLN", issue.RecognizedValue);
        Assert.NotEmpty(issue.SuggestedFix);
    }

    [Fact]
    public async Task CreateAsyncReportsAMissingItemPriceAsAnItemLevelIssue()
    {
        var created = await CreateDraftAsync(
            4m,
            [
                new PurchaseItemCommand("ITEM A", 1, null, 4m, 4m),
                new PurchaseItemCommand("ITEM B", 1, null, null, null)
            ]);

        var issue = Assert.Single(created.Issues);
        Assert.Equal(PurchaseDraftIssueType.MissingItemPrice, issue.Type);
        Assert.Equal(1, issue.ItemPosition);
        Assert.Equal("ITEM B", issue.ItemName);
        Assert.Equal(string.Empty, issue.RecognizedValue);
        Assert.NotEmpty(issue.SuggestedFix);
    }

    [Fact]
    public async Task CreateAsyncReportsAMissingUnitPriceEvenWhenTheLineTotalIsKnown()
    {
        var created = await CreateDraftAsync(
            9m,
            [
                new PurchaseItemCommand("ITEM A", 1, null, 4m, 4m),
                new PurchaseItemCommand("ITEM B", 1, null, null, 5m)
            ]);

        var issue = Assert.Single(created.Issues);
        Assert.Equal(PurchaseDraftIssueType.MissingItemPrice, issue.Type);
        Assert.Equal(1, issue.ItemPosition);

        // The line total is what was recognized, so the card can show it even
        // though the unit price behind it is unknown.
        Assert.Equal("5.00 PLN", issue.RecognizedValue);
    }

    [Fact]
    public async Task CreateAsyncReportsBothIssueTypesTogether()
    {
        var created = await CreateDraftAsync(
            99m,
            [
                new PurchaseItemCommand("ITEM A", 1, null, 4m, 4m),
                new PurchaseItemCommand("ITEM B", 1, null, null, 5m)
            ]);

        Assert.Collection(
            created.Issues,
            issue =>
            {
                Assert.Equal(PurchaseDraftIssueType.MissingItemPrice, issue.Type);
                Assert.Equal(1, issue.ItemPosition);
            },
            issue =>
            {
                Assert.Equal(PurchaseDraftIssueType.ReceiptTotalMismatch, issue.Type);
                Assert.Null(issue.ItemPosition);
            });
    }

    private static async Task<PurchaseDraftView> CreateDraftAsync(
        decimal receiptTotal,
        IReadOnlyList<PurchaseItemCommand> items)
    {
        await using var application = await TestApplication.CreateAsync();
        await using var scope = application.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IPurchaseDraftService>();

        return await service.CreateAsync(
            new CreatePurchaseDraftCommand("Issue test", null, "PLN", receiptTotal, items),
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CreateAsyncRejectsEquivalentPurchaseByDefault()
    {
        await using var application = await TestApplication.CreateAsync();
        await using var scope = application.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IPurchaseDraftService>();
        var command = CreateDuplicateTestCommand();

        var first = await service.CreateAsync(command, TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<PossibleDuplicatePurchaseException>(() =>
            service.CreateAsync(command, TestContext.Current.CancellationToken));

        Assert.Equal(first.PurchaseId, exception.ExistingPurchaseId);

        var matches = await service.SearchAsync("Duplicate test", TestContext.Current.CancellationToken);
        Assert.Single(matches);
    }

    [Fact]
    public async Task CreateAsyncAllowsEquivalentPurchaseAfterExplicitOverride()
    {
        await using var application = await TestApplication.CreateAsync();
        await using var scope = application.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IPurchaseDraftService>();
        var command = CreateDuplicateTestCommand();

        var first = await service.CreateAsync(command, TestContext.Current.CancellationToken);
        var second = await service.CreateAsync(
            command with { AllowPossibleDuplicate = true },
            TestContext.Current.CancellationToken);

        Assert.NotEqual(first.PurchaseId, second.PurchaseId);

        var matches = await service.SearchAsync("Duplicate test", TestContext.Current.CancellationToken);
        Assert.Equal(2, matches.Count);
    }

    private static CreatePurchaseDraftCommand CreateDuplicateTestCommand()
    {
        return new CreatePurchaseDraftCommand(
            "Duplicate test",
            new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.FromHours(2)),
            "PLN",
            4.99m,
            [new PurchaseItemCommand("SAME ITEM", 1, "pcs", 4.99m, 4.99m)]);
    }
}
