using Microsoft.EntityFrameworkCore;
using PurchaseMemory.Domain.Purchases;
using PurchaseMemory.Domain.Receipts;
using PurchaseMemory.Infrastructure.Persistence;

namespace PurchaseMemory.IntegrationTests;

public sealed class ReceiptLineKindMigrationTests
{
    private const string MigrationBeforeLineKind = "20260913001326_AddProductCatalog";

    [Fact]
    public async Task RowsSavedBeforeTheMigrationStayProducts()
    {
        var directory = CreateDirectory();
        try
        {
            var options = Options(directory);
            var purchaseId = Guid.NewGuid();
            var receiptId = Guid.NewGuid();
            await using (var oldContext = new PurchaseMemoryDbContext(options))
            {
                await oldContext.Database.MigrateAsync(MigrationBeforeLineKind, TestContext.Current.CancellationToken);
                await oldContext.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO purchase_drafts (Id, CreatedAt, UpdatedAt, MerchantName, ReceiptTotal)
                    VALUES ({purchaseId}, {1_785_758_400_000L}, {1_785_758_400_000L}, {"Lidl"}, {"4.49"});
                    INSERT INTO purchase_items (Id, PurchaseDraftId, Position, Name, Quantity, UnitPrice, LineTotal)
                    VALUES ({Guid.NewGuid()}, {purchaseId}, {0}, {"Mleko"}, {"1"}, {"4.49"}, {"4.49"});
                    INSERT INTO receipt_captures
                        (Id, ImageStorageKey, ImageContentType, ImageSizeBytes, ImageSha256, Status, CreatedAt, UpdatedAt)
                    VALUES
                        ({receiptId}, {"old/receipt.jpg"}, {"image/jpeg"}, {321L}, {new string('b', 64)},
                         {0}, {1_785_758_400_000L}, {1_785_758_400_000L});
                    INSERT INTO receipt_capture_items (Id, ReceiptCaptureId, Position, Name, LineTotal)
                    VALUES ({Guid.NewGuid()}, {receiptId}, {0}, {"Chleb"}, {"3.00"});
                    """, TestContext.Current.CancellationToken);
            }

            await using var upgraded = new PurchaseMemoryDbContext(options);
            await upgraded.Database.MigrateAsync(TestContext.Current.CancellationToken);

            var purchase = await upgraded.PurchaseDrafts
                .Include(draft => draft.Items)
                .SingleAsync(draft => draft.Id == purchaseId, TestContext.Current.CancellationToken);
            var item = Assert.Single(purchase.Items);
            Assert.Equal(ReceiptLineKind.Product, item.Kind);
            Assert.Null(item.DiscountedItemPosition);

            var capture = await upgraded.ReceiptCaptures
                .Include(receipt => receipt.Items)
                .SingleAsync(receipt => receipt.Id == receiptId, TestContext.Current.CancellationToken);
            var captureItem = Assert.Single(capture.Items);
            Assert.Equal(ReceiptLineKind.Product, captureItem.Kind);
            Assert.Null(captureItem.DiscountedItemPosition);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task FreshDatabaseStoresDiscountLineWithItsLink()
    {
        var directory = CreateDirectory();
        try
        {
            var options = Options(directory);
            var now = new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);
            var draft = PurchaseDraft.Create(
                "Lidl",
                now,
                "PLN",
                3.49m,
                [
                    PurchaseItem.Create(0, "Mleko", 1m, null, 4.49m, 4.49m),
                    PurchaseItem.Create(1, "OPUST", null, null, null, -1m, ReceiptLineKind.Discount, 0)
                ],
                now);

            await using (var context = new PurchaseMemoryDbContext(options))
            {
                await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
                context.PurchaseDrafts.Add(draft);
                await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            }

            await using var reloaded = new PurchaseMemoryDbContext(options);
            var items = (await reloaded.PurchaseDrafts
                    .Include(purchase => purchase.Items)
                    .SingleAsync(purchase => purchase.Id == draft.Id, TestContext.Current.CancellationToken))
                .Items.OrderBy(item => item.Position).ToList();
            Assert.Equal(ReceiptLineKind.Product, items[0].Kind);
            Assert.Equal(ReceiptLineKind.Discount, items[1].Kind);
            Assert.Equal(0, items[1].DiscountedItemPosition);
            Assert.Equal(-1m, items[1].LineTotal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "purchase-memory-migration-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static DbContextOptions<PurchaseMemoryDbContext> Options(string directory) =>
        new DbContextOptionsBuilder<PurchaseMemoryDbContext>()
            .UseSqlite($"Data Source={Path.Combine(directory, "migration.db")};Pooling=False")
            .Options;
}
