using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PurchaseMemory.Domain.Receipts;
using PurchaseMemory.Infrastructure;
using PurchaseMemory.Infrastructure.Persistence;

namespace PurchaseMemory.IntegrationTests;

/// <summary>
/// <c>AddReceiptTextHighlight</c> on a database that already holds receipts:
/// old lines keep the recognition box, new geometry round-trips, and Down /
/// Up again work (SQLite like the owner's file, Postgres like production when
/// <c>PURCHASE_MEMORY_TEST_POSTGRES</c> is set).
/// </summary>
public sealed class ReceiptTextHighlightMigrationTests
{
    private const string SqliteMigrationBefore = "20261016150110_AddProductReviewFlag";
    private const string PostgresMigrationBefore = "20261016150254_AddProductReviewFlag";

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task UpgradesDowngradesAndUpgradesAgainOnSqlite()
    {
        var directory = Path.Combine(Path.GetTempPath(), "purchase-memory-migration-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var options = new DbContextOptionsBuilder<PurchaseMemoryDbContext>()
                .UseSqlite($"Data Source={Path.Combine(directory, "migration.db")};Pooling=False")
                .Options;
            await RunScenarioAsync(options, SqliteMigrationBefore, postgres: false);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task UpgradesDowngradesAndUpgradesAgainOnPostgres()
    {
        Assert.SkipWhen(
            ExternalTestServices.PostgresAdminConnectionString is null,
            $"Set {ExternalTestServices.PostgresVariable} to run against real Postgres.");
        await using var database = PostgresTestDatabase.TryCreate()!;
        var builder = new DbContextOptionsBuilder<PurchaseMemoryDbContext>();
        DependencyInjection.ConfigureDatabase(builder, DatabaseSettings.Postgres(database.ConnectionString));
        await RunScenarioAsync(builder.Options, PostgresMigrationBefore, postgres: true);
    }

    private static async Task RunScenarioAsync(DbContextOptions<PurchaseMemoryDbContext> options, string before, bool postgres)
    {
        const long created = 1_785_758_400_000L;
        var receiptId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        await using (var oldContext = new PurchaseMemoryDbContext(options))
        {
            await oldContext.Database.MigrateAsync(before, Cancel);
            await oldContext.Database.ExecuteSqlRawAsync(
                Dialect("""
                    INSERT INTO [[receipt_captures]] ([Id], [ImageStorageKey], [ImageContentType], [ImageSizeBytes], [ImageSha256], [Status], [CreatedAt], [UpdatedAt])
                    VALUES ({0}, 'old/receipt.jpg', 'image/jpeg', 321, {1}, 5, {2}, {2});
                    INSERT INTO [[receipt_image_parts]] ([Id], [ReceiptCaptureId], [Position], [StorageKey], [ContentType], [SizeBytes], [Sha256])
                    VALUES ({3}, {0}, 0, 'old/receipt.jpg', 'image/jpeg', 321, {1});
                    INSERT INTO [[receipt_capture_items]] ([Id], [ReceiptCaptureId], [Position], [Name], [LineTotal], [Kind])
                    VALUES ({4}, {0}, 0, 'Chleb', 3.00, 'Product');
                    """, postgres),
                [receiptId, new string('c', 64), created, Guid.NewGuid(), itemId],
                Cancel);
        }

        await using (var upgraded = new PurchaseMemoryDbContext(options))
        {
            await upgraded.Database.MigrateAsync(Cancel);
            var receipt = await upgraded.ReceiptCaptures.Include(capture => capture.Items)
                .SingleAsync(capture => capture.Id == receiptId, Cancel);
            Assert.Equal(ReceiptTextLocationStatus.NotRequested, receipt.TextLocationStatus);
            Assert.Equal(0, receipt.TextLocationAttempts);
            Assert.Null(receipt.TextLocationAttemptedAt);
            var item = Assert.Single(receipt.Items);
            Assert.Equal(ReceiptBoxSource.Recognition, item.BoxSource);
            Assert.Empty(item.TextPolygons);

            // New geometry round-trips through the new columns and table.
            var polygon = TextPolygon.TryCreate([0.1, 0.2, 0.5, 0.2, 0.5, 0.25, 0.1, 0.25])!;
            item.ShowPrintedText([polygon]);
            receipt.BeginTextLocationCall(DateTimeOffset.FromUnixTimeMilliseconds(created));
            receipt.RecordTextLocated();
            upgraded.ReceiptTextLayouts.Add(ReceiptTextLayout.Create(
                receiptId,
                "azure:prebuilt-read:test",
                1000,
                2000,
                [new ReceiptTextLine("Chleb 3,00", [10, 10, 200, 10, 200, 30, 10, 30], [new ReceiptTextWord("Chleb", [10, 10, 90, 10, 90, 30, 10, 30])])],
                DateTimeOffset.FromUnixTimeMilliseconds(created)));
            await upgraded.SaveChangesAsync(Cancel);
        }

        await using (var reader = new PurchaseMemoryDbContext(options))
        {
            var item = await reader.Set<ReceiptCaptureItem>().AsNoTracking().SingleAsync(entry => entry.Id == itemId, Cancel);
            Assert.Equal(ReceiptBoxSource.Azure, item.BoxSource);
            Assert.Equal([0.1, 0.2, 0.5, 0.2, 0.5, 0.25, 0.1, 0.25], Assert.Single(item.TextPolygons).Points);
            var layout = await reader.ReceiptTextLayouts.AsNoTracking().SingleAsync(entry => entry.ReceiptCaptureId == receiptId, Cancel);
            Assert.Equal("Chleb", Assert.Single(Assert.Single(layout.Lines).Words).Text);
            Assert.Equal(ReceiptTextLocationStatus.Located,
                (await reader.ReceiptCaptures.AsNoTracking().SingleAsync(capture => capture.Id == receiptId, Cancel)).TextLocationStatus);
        }

        await using (var downgraded = new PurchaseMemoryDbContext(options))
        {
            await downgraded.GetService<IMigrator>().MigrateAsync(before, cancellationToken: Cancel);
            Assert.False(await ColumnExistsAsync(downgraded, "receipt_capture_items", "TextPolygons", postgres));
            Assert.False(await ColumnExistsAsync(downgraded, "receipt_captures", "TextLocationStatus", postgres));
            Assert.False(await TableExistsAsync(downgraded, "receipt_text_layouts", postgres));
            Assert.Equal(1, await CountAsync(downgraded, Dialect("SELECT COUNT(*) AS [Value] FROM [[receipt_capture_items]]", postgres)));
            Assert.Equal(1, await CountAsync(downgraded, Dialect("SELECT COUNT(*) AS [Value] FROM [[receipt_captures]]", postgres)));
        }

        await using (var again = new PurchaseMemoryDbContext(options))
        {
            await again.Database.MigrateAsync(Cancel);
            Assert.Empty(await again.Database.GetPendingMigrationsAsync(Cancel));
            var item = await again.Set<ReceiptCaptureItem>().AsNoTracking().SingleAsync(entry => entry.Id == itemId, Cancel);
            Assert.Equal(ReceiptBoxSource.Recognition, item.BoxSource);
            Assert.Empty(item.TextPolygons);
        }
    }

    private static async Task<bool> ColumnExistsAsync(PurchaseMemoryDbContext context, string table, string column, bool postgres) =>
        await CountAsync(context, postgres
            ? $"SELECT COUNT(*) AS \"Value\" FROM information_schema.columns WHERE table_schema = '{PurchaseMemoryDbContext.PostgresSchema}' AND table_name = '{table}' AND column_name = '{column}'"
            : $"SELECT COUNT(*) AS Value FROM pragma_table_info('{table}') WHERE name = '{column}'") > 0;

    private static async Task<bool> TableExistsAsync(PurchaseMemoryDbContext context, string table, bool postgres) =>
        await CountAsync(context, postgres
            ? $"SELECT COUNT(*) AS \"Value\" FROM information_schema.tables WHERE table_schema = '{PurchaseMemoryDbContext.PostgresSchema}' AND table_name = '{table}'"
            : $"SELECT COUNT(*) AS Value FROM sqlite_master WHERE type = 'table' AND name = '{table}'") > 0;

    private static async Task<long> CountAsync(PurchaseMemoryDbContext context, string sql) =>
        await context.Database.SqlQueryRaw<long>(sql).SingleAsync(Cancel);

    private static string Dialect(string template, bool postgres) => postgres
        ? template
            .Replace("[[", $"{PurchaseMemoryDbContext.PostgresSchema}.\"", StringComparison.Ordinal)
            .Replace("]]", "\"", StringComparison.Ordinal)
            .Replace("[", "\"", StringComparison.Ordinal)
            .Replace("]", "\"", StringComparison.Ordinal)
        : template
            .Replace("[[", string.Empty, StringComparison.Ordinal)
            .Replace("]]", string.Empty, StringComparison.Ordinal)
            .Replace("[", string.Empty, StringComparison.Ordinal)
            .Replace("]", string.Empty, StringComparison.Ordinal);
}
