using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PurchaseMemory.Application.Products;
using PurchaseMemory.Domain.Products;
using PurchaseMemory.Infrastructure;
using PurchaseMemory.Infrastructure.Persistence;

namespace PurchaseMemory.IntegrationTests;

/// <summary>
/// <c>AddProductDisplayNames</c> on a database that already holds classified
/// products: nothing is rewritten (names stay empty, nobody decided, never
/// asked), new names round-trip, and Down / Up again work (SQLite like the
/// owner's file, Postgres like production when <c>PURCHASE_MEMORY_TEST_POSTGRES</c> is set).
/// </summary>
public sealed class ProductDisplayNamesMigrationTests
{
    private const string SqliteMigrationBefore = "20261018155538_AddReceiptTextHighlight";
    private const string PostgresMigrationBefore = "20261018155541_AddReceiptTextHighlight";
    private static readonly string[] NewColumns =
        ["NamePl", "NormalizedNamePl", "NamePlSource", "NameRu", "NormalizedNameRu", "NameRuSource", "NamingAttemptedAt"];

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

        await using (var oldContext = new PurchaseMemoryDbContext(options))
        {
            await oldContext.Database.MigrateAsync(before, Cancel);
            await oldContext.Database.ExecuteSqlRawAsync(
                Dialect("""
                    INSERT INTO [[products]] ([Id], [CanonicalName], [NormalizedName], [CreatedAt], [UpdatedAt], [CategorySource], [Brand], [NormalizedBrand], [BrandSource], [Origin], [NeedsReview])
                    VALUES ({0}, 'ACTIV KEFI NAT 280', 'activ kefi nat 280', {1}, {1}, 'Suggested', 'Activia', 'activia', 'Confirmed', 'Receipt', FALSE);
                    """, postgres),
                [productId, created],
                Cancel);
        }

        await using (var upgraded = new PurchaseMemoryDbContext(options))
        {
            await upgraded.Database.MigrateAsync(Cancel);
            var product = await upgraded.Products.SingleAsync(entry => entry.Id == productId, Cancel);
            Assert.Equal("ACTIV KEFI NAT 280", product.CanonicalName);
            Assert.Equal(("Activia", ProductAttributeSource.Confirmed), (product.Brand, product.BrandSource));
            Assert.Null(product.NamePl);
            Assert.Null(product.NamePlSource);
            Assert.Null(product.NameRu);
            Assert.Null(product.NameRuSource);
            Assert.Null(product.NamingAttemptedAt);

            product.ConfirmName(ProductNameLanguage.Polish, "Kefir naturalny Activia 280 g", ProductNameNormalizer.Normalize("Kefir naturalny Activia 280 g"), DateTimeOffset.FromUnixTimeMilliseconds(created));
            product.ConfirmName(ProductNameLanguage.Russian, "Кефир натуральный Activia, 280 г", ProductNameNormalizer.Normalize("Кефир натуральный Activia, 280 г"), DateTimeOffset.FromUnixTimeMilliseconds(created));
            await upgraded.SaveChangesAsync(Cancel);
        }

        await using (var reader = new PurchaseMemoryDbContext(options))
        {
            var product = await reader.Products.AsNoTracking().SingleAsync(entry => entry.Id == productId, Cancel);
            Assert.Equal(("Kefir naturalny Activia 280 g", ProductAttributeSource.Confirmed), (product.NamePl, product.NamePlSource));
            Assert.Equal(("Кефир натуральный Activia, 280 г", "кефир натуральныи activia 280 г"), (product.NameRu, product.NormalizedNameRu));
            // Cyrillic is matched by the stored key, not by a database collation.
            Assert.Equal(productId, (await reader.Products.AsNoTracking().SingleAsync(entry => entry.NormalizedNameRu!.Contains("кефир"), Cancel)).Id);
        }

        await using (var downgraded = new PurchaseMemoryDbContext(options))
        {
            await downgraded.GetService<IMigrator>().MigrateAsync(before, cancellationToken: Cancel);
            foreach (var column in NewColumns)
            {
                Assert.False(await ColumnExistsAsync(downgraded, "products", column, postgres));
            }

            Assert.Equal(1, await CountAsync(downgraded, Dialect("SELECT COUNT(*) AS [Value] FROM [[products]]", postgres)));
        }

        await using (var again = new PurchaseMemoryDbContext(options))
        {
            await again.Database.MigrateAsync(Cancel);
            Assert.Empty(await again.Database.GetPendingMigrationsAsync(Cancel));
            var product = await again.Products.AsNoTracking().SingleAsync(entry => entry.Id == productId, Cancel);
            Assert.Null(product.NamePl);
            Assert.Equal("ACTIV KEFI NAT 280", product.CanonicalName);
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
