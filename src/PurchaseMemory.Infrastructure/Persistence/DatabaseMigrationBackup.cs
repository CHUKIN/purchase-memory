using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace PurchaseMemory.Infrastructure.Persistence;

/// <summary>
/// Copies the SQLite file aside before pending migrations touch it, so a bad
/// migration can never cost the only copy of a user's purchase history.
/// </summary>
public static partial class DatabaseMigrationBackup
{
    public const string BackupDirectoryName = "backups";

    /// <summary>
    /// Returns the backup path, or null when there is nothing worth copying (no
    /// file yet, or an empty file). Any failure is rethrown as
    /// <see cref="DatabaseBackupException"/>: the caller must not migrate then.
    /// An existing backup is never overwritten.
    /// </summary>
    public static async Task<string?> CreateAsync(
        string databasePath,
        string targetMigrationId,
        DateTimeOffset now,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetMigrationId);
        ArgumentNullException.ThrowIfNull(logger);

        var source = new FileInfo(databasePath);
        if (!source.Exists || source.Length == 0)
        {
            return null;
        }

        var directory = Path.Combine(source.DirectoryName!, BackupDirectoryName);
        var stem = Path.GetFileNameWithoutExtension(source.Name);
        var stamp = now.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var backupPath = Path.Combine(directory, $"{stem}-{stamp}-before-{targetMigrationId}.db");

        try
        {
            Directory.CreateDirectory(directory);
            if (File.Exists(backupPath))
            {
                throw new IOException($"Backup file '{backupPath}' already exists and will not be overwritten.");
            }

            // SQLite's online backup copies a consistent snapshot even if a
            // journal is pending, which a raw file copy cannot guarantee.
            await using (var sourceConnection = new SqliteConnection(
                new SqliteConnectionStringBuilder { DataSource = source.FullName, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
            await using (var backupConnection = new SqliteConnection(
                new SqliteConnectionStringBuilder { DataSource = backupPath, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString()))
            {
                await sourceConnection.OpenAsync(cancellationToken);
                await backupConnection.OpenAsync(cancellationToken);
                sourceConnection.BackupDatabase(backupConnection);

                // The copy inherits WAL mode; switch it to a rollback journal so
                // the backup is one self-contained file with no -wal/-shm beside it.
                await using var journal = backupConnection.CreateCommand();
                journal.CommandText = "PRAGMA journal_mode=DELETE;";
                await journal.ExecuteNonQueryAsync(cancellationToken);
            }

            BackupCreated(logger, backupPath, targetMigrationId);
            return backupPath;
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not DatabaseBackupException)
        {
            BackupFailed(logger, exception, backupPath);
            throw new DatabaseBackupException(
                $"Could not back up the database to '{backupPath}' before migrating to {targetMigrationId}; "
                + "the migration was not applied. Free disk space or fix permissions for the data directory and restart.",
                exception);
        }
    }

    [LoggerMessage(EventId = 1101, Level = LogLevel.Warning,
        Message = "Database backed up to {BackupPath} before applying migrations up to {MigrationId}")]
    private static partial void BackupCreated(ILogger logger, string backupPath, string migrationId);

    [LoggerMessage(EventId = 1102, Level = LogLevel.Critical,
        Message = "Database backup to {BackupPath} failed; pending migrations were not applied")]
    private static partial void BackupFailed(ILogger logger, Exception exception, string backupPath);
}

public sealed class DatabaseBackupException(string message, Exception innerException)
    : Exception(message, innerException);
