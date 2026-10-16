using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PurchaseMemory.Infrastructure.Persistence;

namespace PurchaseMemory.IntegrationTests;

public sealed class DatabaseMigrationBackupTests
{
    private const string PreviousMigration = "20260913001326_AddProductCatalog";
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 15, 30, 5, TimeSpan.Zero);

    [Fact]
    public async Task PendingMigrationsBackUpTheExistingFileFirst()
    {
        var directory = CreateDirectory();
        try
        {
            var databasePath = Path.Combine(directory, "purchases.db");
            await CreateOldDatabaseAsync(databasePath);

            await using (var provider = Services(databasePath))
            {
                await provider.EnsureDatabaseMigratedAsync(TestContext.Current.CancellationToken);
            }

            var backup = Assert.Single(Directory.GetFiles(Path.Combine(directory, "backups")));
            Assert.Equal("purchases-20260924T153005Z-before-20261016150110_AddProductReviewFlag.db", Path.GetFileName(backup));
            Assert.Equal(PreviousMigration, await LastMigrationAsync(backup));
            Assert.Equal("20261016150110_AddProductReviewFlag", await LastMigrationAsync(databasePath));

            // Nothing pending any more: a restart must not stack backups.
            await using (var provider = Services(databasePath))
            {
                await provider.EnsureDatabaseMigratedAsync(TestContext.Current.CancellationToken);
            }

            Assert.Single(Directory.GetFiles(Path.Combine(directory, "backups")));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task AFreshDatabaseNeedsNoBackup()
    {
        var directory = CreateDirectory();
        try
        {
            await using var provider = Services(Path.Combine(directory, "purchases.db"));
            await provider.EnsureDatabaseMigratedAsync(TestContext.Current.CancellationToken);

            Assert.False(Directory.Exists(Path.Combine(directory, "backups")));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task AFailedBackupStopsTheMigration()
    {
        var directory = CreateDirectory();
        try
        {
            var databasePath = Path.Combine(directory, "purchases.db");
            await CreateOldDatabaseAsync(databasePath);
            // A file where the backup directory should be makes the copy fail.
            await File.WriteAllTextAsync(Path.Combine(directory, "backups"), "not a directory", TestContext.Current.CancellationToken);

            await using var provider = Services(databasePath);
            var exception = await Assert.ThrowsAsync<DatabaseBackupException>(
                () => provider.EnsureDatabaseMigratedAsync(TestContext.Current.CancellationToken));

            Assert.Contains("migration was not applied", exception.Message, StringComparison.Ordinal);
            Assert.Equal(PreviousMigration, await LastMigrationAsync(databasePath));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task AnExistingBackupIsNeverOverwritten()
    {
        var directory = CreateDirectory();
        try
        {
            var databasePath = Path.Combine(directory, "purchases.db");
            await CreateOldDatabaseAsync(databasePath);
            Directory.CreateDirectory(Path.Combine(directory, "backups"));
            var existing = Path.Combine(directory, "backups", "purchases-20260924T153005Z-before-X.db");
            await File.WriteAllTextAsync(existing, "keep me", TestContext.Current.CancellationToken);

            await Assert.ThrowsAsync<DatabaseBackupException>(() => DatabaseMigrationBackup.CreateAsync(
                databasePath, "X", Now, NullLogger.Instance, TestContext.Current.CancellationToken));

            Assert.Equal("keep me", await File.ReadAllTextAsync(existing, TestContext.Current.CancellationToken));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static ServiceProvider Services(string databasePath)
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
        services.AddDbContext<PurchaseMemoryDbContext>(options => options.UseSqlite($"Data Source={databasePath}"));
        return services.BuildServiceProvider();
    }

    private static async Task CreateOldDatabaseAsync(string databasePath)
    {
        var options = new DbContextOptionsBuilder<PurchaseMemoryDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;
        await using var context = new PurchaseMemoryDbContext(options);
        await context.Database.MigrateAsync(PreviousMigration, TestContext.Current.CancellationToken);
    }

    private static async Task<string> LastMigrationAsync(string databasePath)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC LIMIT 1";
        return (string)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private static string CreateDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "purchase-memory-backup-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
