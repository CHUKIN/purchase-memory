namespace PurchaseMemory.Infrastructure.Persistence;

public enum DatabaseProvider
{
    Sqlite,
    Postgres
}

/// <summary>
/// Which database the app talks to. SQLite is the local default; Postgres
/// (Supabase) is chosen with <c>Database__Provider=Postgres</c> and
/// <c>ConnectionStrings__Postgres</c>.
/// </summary>
public sealed record DatabaseSettings(DatabaseProvider Provider, string ConnectionString)
{
    /// <summary>
    /// Postgres migrations live in their own assembly: EF keeps one model
    /// snapshot per migrations assembly, and the SQLite history must stay intact.
    /// </summary>
    public const string PostgresMigrationsAssembly = "PurchaseMemory.Infrastructure.Postgres";

    public static DatabaseSettings Sqlite(string connectionString) => new(DatabaseProvider.Sqlite, connectionString);

    public static DatabaseSettings Postgres(string connectionString) => new(DatabaseProvider.Postgres, connectionString);

    /// <summary>Safe for logs: never includes the password or other secrets.</summary>
    public string Describe() => Provider switch
    {
        DatabaseProvider.Postgres => DescribePostgres(ConnectionString),
        _ => $"SQLite {new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(ConnectionString).DataSource}"
    };

    /// <summary>Records print their members; the connection string holds a password.</summary>
    public override string ToString() => Describe();

    private static string DescribePostgres(string connectionString)
    {
        try
        {
            var builder = new Npgsql.NpgsqlConnectionStringBuilder(connectionString);
            return $"Postgres {builder.Host}:{builder.Port}/{builder.Database} (SSL {builder.SslMode})";
        }
        catch (ArgumentException)
        {
            return "Postgres (unparsable connection string)";
        }
    }
}
