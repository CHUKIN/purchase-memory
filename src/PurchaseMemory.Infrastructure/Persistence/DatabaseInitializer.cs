using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using System.Data.Common;

namespace PurchaseMemory.Infrastructure.Persistence;

public static partial class DatabaseInitializer
{
    private const string InitialMigrationId = "20260715214748_InitialSchema";

    /// <summary>
    /// Migrates both fresh databases and the schema created by the first spike.
    /// The legacy bootstrap is intentionally isolated here and can be removed
    /// after every local installation has an EF migrations history table.
    /// </summary>
    public static async Task EnsureDatabaseMigratedAsync(
        this IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>();
        if (!dbContext.Database.IsSqlite())
        {
            await MigrateServerDatabaseAsync(scope.ServiceProvider, dbContext, cancellationToken);
            return;
        }

        await BackUpBeforePendingMigrationsAsync(scope.ServiceProvider, dbContext, cancellationToken);
        await BootstrapLegacySchemaAsync(dbContext, cancellationToken);
        await dbContext.Database.MigrateAsync(cancellationToken);
    }

    /// <summary>
    /// A server database (Postgres/Supabase) has no file to copy aside: backups
    /// are the hosting provider's job, so the pending list is logged instead.
    /// The SQLite-only legacy bootstrap never applies to a server database.
    /// </summary>
    private static async Task MigrateServerDatabaseAsync(
        IServiceProvider services,
        PurchaseMemoryDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var pending = (await dbContext.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pending.Count > 0)
        {
            var logger = services.GetService<ILoggerFactory>()?.CreateLogger(typeof(DatabaseInitializer).FullName!)
                ?? NullLogger.Instance;
            ServerMigrationsPending(logger, dbContext.Database.ProviderName ?? "unknown", pending.Count, string.Join(", ", pending));
        }

        await dbContext.Database.MigrateAsync(cancellationToken);
    }

    [LoggerMessage(EventId = 1103, Level = LogLevel.Warning,
        Message = "Applying {Count} pending migration(s) to the {Provider} database without a file backup "
            + "(server databases rely on the hosting provider's backups): {Migrations}")]
    private static partial void ServerMigrationsPending(ILogger logger, string provider, int count, string migrations);

    /// <summary>
    /// Runs before anything writes to the file, including the legacy
    /// bootstrap. A failed backup propagates and stops startup.
    /// </summary>
    private static async Task BackUpBeforePendingMigrationsAsync(
        IServiceProvider services,
        PurchaseMemoryDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var databasePath = new SqliteConnectionStringBuilder(dbContext.Database.GetConnectionString()).DataSource;
        if (string.IsNullOrWhiteSpace(databasePath)
            || databasePath == ":memory:"
            || !File.Exists(databasePath))
        {
            return;
        }

        var pending = (await dbContext.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pending.Count == 0)
        {
            return;
        }

        var logger = services.GetService<ILoggerFactory>()?.CreateLogger(typeof(DatabaseInitializer).FullName!)
            ?? NullLogger.Instance;
        var now = (services.GetService<TimeProvider>() ?? TimeProvider.System).GetUtcNow();
        await DatabaseMigrationBackup.CreateAsync(databasePath, pending[^1], now, logger, cancellationToken);
    }

    private static async Task BootstrapLegacySchemaAsync(
        PurchaseMemoryDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);
        try
        {
            var hasPurchaseTable = await TableExistsAsync(connection, "purchase_drafts", cancellationToken);
            var hasMigrationHistory = await TableExistsAsync(connection, "__EFMigrationsHistory", cancellationToken);
            if (!hasPurchaseTable || hasMigrationHistory)
            {
                return;
            }

            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await ExecuteAsync(
                connection,
                transaction,
                """
                ALTER TABLE purchase_drafts ADD COLUMN SourceReceiptId TEXT NULL;
                CREATE UNIQUE INDEX IX_purchase_drafts_SourceReceiptId ON purchase_drafts (SourceReceiptId);

                CREATE TABLE receipt_captures (
                    Id TEXT NOT NULL CONSTRAINT PK_receipt_captures PRIMARY KEY,
                    ImageStorageKey TEXT NOT NULL,
                    ImageContentType TEXT NOT NULL,
                    ImageSizeBytes INTEGER NOT NULL,
                    ImageSha256 TEXT NOT NULL,
                    Status INTEGER NOT NULL,
                    MerchantName TEXT NULL,
                    PurchasedAt INTEGER NULL,
                    Currency TEXT NULL,
                    ReceiptTotal TEXT NULL,
                    ExtractorName TEXT NULL,
                    ExtractorModel TEXT NULL,
                    ErrorMessage TEXT NULL,
                    SavedPurchaseId TEXT NULL,
                    CreatedAt INTEGER NOT NULL,
                    UpdatedAt INTEGER NOT NULL
                );

                CREATE TABLE receipt_capture_items (
                    Id TEXT NOT NULL CONSTRAINT PK_receipt_capture_items PRIMARY KEY,
                    Position INTEGER NOT NULL,
                    Name TEXT NOT NULL,
                    Quantity TEXT NULL,
                    Unit TEXT NULL,
                    UnitPrice TEXT NULL,
                    LineTotal TEXT NULL,
                    ReceiptCaptureId TEXT NOT NULL,
                    CONSTRAINT FK_receipt_capture_items_receipt_captures_ReceiptCaptureId
                        FOREIGN KEY (ReceiptCaptureId) REFERENCES receipt_captures (Id) ON DELETE CASCADE
                );

                CREATE UNIQUE INDEX IX_receipt_capture_items_ReceiptCaptureId_Position
                    ON receipt_capture_items (ReceiptCaptureId, Position);
                CREATE INDEX IX_receipt_captures_CreatedAt ON receipt_captures (CreatedAt);
                CREATE UNIQUE INDEX IX_receipt_captures_ImageSha256 ON receipt_captures (ImageSha256);
                CREATE INDEX IX_receipt_captures_Status ON receipt_captures (Status);

                CREATE TABLE __EFMigrationsHistory (
                    MigrationId TEXT NOT NULL CONSTRAINT PK___EFMigrationsHistory PRIMARY KEY,
                    ProductVersion TEXT NOT NULL
                );
                """,
                cancellationToken);

            await using var historyCommand = connection.CreateCommand();
            historyCommand.Transaction = transaction;
            historyCommand.CommandText =
                "INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ($migrationId, $productVersion);";
            AddParameter(historyCommand, "$migrationId", InitialMigrationId);
            AddParameter(historyCommand, "$productVersion", "10.0.9");
            await historyCommand.ExecuteNonQueryAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    private static async Task<bool> TableExistsAsync(
        DbConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $tableName;";
        AddParameter(command, "$tableName", tableName);
        var count = Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken),
            System.Globalization.CultureInfo.InvariantCulture);
        return count > 0;
    }

    private static async Task ExecuteAsync(
        DbConnection connection,
        DbTransaction transaction,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
