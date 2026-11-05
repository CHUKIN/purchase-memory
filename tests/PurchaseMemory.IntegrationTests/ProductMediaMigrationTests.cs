using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PurchaseMemory.Domain.Products;
using PurchaseMemory.Infrastructure;
using PurchaseMemory.Infrastructure.Persistence;

namespace PurchaseMemory.IntegrationTests;

/// <summary>
/// <c>AddProductMedia</c> on a database that already holds products: they stay
/// as they were (no avoid mark), photos, barcodes and remembered codes
/// round-trip with their unique indexes, and Down / Up again work (SQLite like
/// the owner's file, Postgres like production when
/// <c>PURCHASE_MEMORY_TEST_POSTGRES</c> is set).
/// </summary>
public sealed class ProductMediaMigrationTests
{
    private const string SqliteMigrationBefore = "20261029214537_AddProductDisplayNames";
    private const string PostgresMigrationBefore = "20261029214546_AddProductDisplayNames";
    private static readonly string[] NewTables = ["product_photos", "product_barcodes", "remembered_barcodes"];

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
        var productId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var now = DateTimeOffset.FromUnixTimeMilliseconds(created);

        await using (var oldContext = new PurchaseMemoryDbContext(options))
        {
            await oldContext.Database.MigrateAsync(before, Cancel);
            foreach (var (id, name) in new[] { (productId, "KEFIR NAT"), (otherId, "MLEKO") })
            {
                await oldContext.Database.ExecuteSqlRawAsync(
                    Dialect("""
                        INSERT INTO [[products]] ([Id], [CanonicalName], [NormalizedName], [CreatedAt], [UpdatedAt], [Origin], [NeedsReview])
                        VALUES ({0}, {1}, {2}, {3}, {3}, 'Receipt', FALSE);
                        """, postgres),
                    [id, name, name.ToLowerInvariant(), created],
                    Cancel);
            }
        }

        await using (var upgraded = new PurchaseMemoryDbContext(options))
        {
            await upgraded.Database.MigrateAsync(Cancel);
            var product = await upgraded.Products.SingleAsync(entry => entry.Id == productId, Cancel);
            Assert.Equal("KEFIR NAT", product.CanonicalName);
            Assert.Null(product.AvoidSince);

            product.SetAvoid(true, now, now);
            upgraded.ProductPhotos.Add(ProductPhoto.Create(
                Guid.NewGuid(), productId, new StoredProductPhotoFiles("products/a/photo.jpg", "products/a/thumb.jpg", 1280, 960, 200_000, 15_000),
                isMain: true, "device-1", "user-1", "anna@example.com", now));
            upgraded.ProductBarcodes.Add(ProductBarcode.Create(productId, "5900512320045", ProductBarcodeSource.Scan, "user-1", null, now));
            var remembered = RememberedBarcode.Create("96385074");
            remembered.Remember("user-1", now, "Kefir", null, null, null);
            upgraded.RememberedBarcodes.Add(remembered);
            await upgraded.SaveChangesAsync(Cancel);
        }

        await using (var unique = new PurchaseMemoryDbContext(options))
        {
            // One code, one product; one device photo id per product.
            unique.ProductBarcodes.Add(ProductBarcode.Create(otherId, "5900512320045", ProductBarcodeSource.Manual, "user-2", null, now));
            await Assert.ThrowsAsync<DbUpdateException>(() => unique.SaveChangesAsync(Cancel));
        }

        await using (var unique = new PurchaseMemoryDbContext(options))
        {
            unique.ProductPhotos.Add(ProductPhoto.Create(
                Guid.NewGuid(), productId, new StoredProductPhotoFiles("products/b/photo.jpg", "products/b/thumb.jpg", 10, 10, 1, 1),
                isMain: false, "device-1", "user-1", null, now));
            await Assert.ThrowsAsync<DbUpdateException>(() => unique.SaveChangesAsync(Cancel));
        }

        await using (var reader = new PurchaseMemoryDbContext(options))
        {
            var product = await reader.Products.AsNoTracking().SingleAsync(entry => entry.Id == productId, Cancel);
            Assert.Equal(now, product.AvoidSince);
            var photo = await reader.ProductPhotos.AsNoTracking().SingleAsync(Cancel);
            Assert.Equal((productId, true, "device-1", now), (photo.ProductId, photo.IsMain, photo.ClientId, photo.CreatedAt));
            Assert.Equal(ProductBarcodeSource.Scan, (await reader.ProductBarcodes.AsNoTracking().SingleAsync(Cancel)).Source);
            Assert.Equal("Kefir", (await reader.RememberedBarcodes.AsNoTracking().SingleAsync(Cancel)).OffName);
        }

        await using (var downgraded = new PurchaseMemoryDbContext(options))
        {
            await downgraded.GetService<IMigrator>().MigrateAsync(before, cancellationToken: Cancel);
            foreach (var table in NewTables)
            {
                Assert.False(await TableExistsAsync(downgraded, table, postgres));
            }

            Assert.False(await ColumnExistsAsync(downgraded, "products", "AvoidSince", postgres));
            Assert.Equal(2, await CountAsync(downgraded, Dialect("SELECT COUNT(*) AS [Value] FROM [[products]]", postgres)));
        }

        await using (var again = new PurchaseMemoryDbContext(options))
        {
            await again.Database.MigrateAsync(Cancel);
            Assert.Empty(await again.Database.GetPendingMigrationsAsync(Cancel));
            Assert.Empty(await again.ProductPhotos.AsNoTracking().ToListAsync(Cancel));
            Assert.Null((await again.Products.AsNoTracking().SingleAsync(entry => entry.Id == productId, Cancel)).AvoidSince);
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
