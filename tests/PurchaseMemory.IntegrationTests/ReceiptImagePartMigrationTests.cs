using Microsoft.EntityFrameworkCore;
using PurchaseMemory.Infrastructure.Persistence;

namespace PurchaseMemory.IntegrationTests;

public sealed class ReceiptImagePartMigrationTests
{
    [Fact]
    public async Task ExistingSingleImageCaptureBecomesPartZero()
    {
        var directory = Path.Combine(Path.GetTempPath(), "purchase-memory-migration-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var databasePath = Path.Combine(directory, "migration.db");
            var options = new DbContextOptionsBuilder<PurchaseMemoryDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;
            var receiptId = Guid.NewGuid();
            await using (var oldContext = new PurchaseMemoryDbContext(options))
            {
                await oldContext.Database.MigrateAsync(
                    "20260715214748_InitialSchema",
                    TestContext.Current.CancellationToken);
                await oldContext.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO receipt_captures
                        (Id, ImageStorageKey, ImageContentType, ImageSizeBytes, ImageSha256, Status,
                         CreatedAt, UpdatedAt)
                    VALUES
                        ({receiptId}, {"legacy/original.jpg"}, {"image/jpeg"}, {321L}, {new string('a', 64)},
                         {0}, {1_785_758_400_000L}, {1_785_758_400_000L});
                    """, TestContext.Current.CancellationToken);
            }

            await using var upgradedContext = new PurchaseMemoryDbContext(options);
            await upgradedContext.Database.MigrateAsync(TestContext.Current.CancellationToken);
            var capture = await upgradedContext.ReceiptCaptures
                .Include(receipt => receipt.ImageParts)
                .SingleAsync(receipt => receipt.Id == receiptId, TestContext.Current.CancellationToken);
            var part = Assert.Single(capture.ImageParts);
            Assert.Equal(0, part.Position);
            Assert.Equal("legacy/original.jpg", part.StorageKey);
            Assert.Equal(new string('a', 64), part.Sha256);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
