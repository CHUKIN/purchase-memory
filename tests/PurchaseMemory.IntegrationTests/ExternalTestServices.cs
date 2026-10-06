using Amazon.S3;
using Amazon.S3.Model;
using Npgsql;
using PurchaseMemory.Infrastructure.Images;

namespace PurchaseMemory.IntegrationTests;

/// <summary>
/// Opt-in real Postgres / S3 backends for the test suite, started with
/// <c>deploy/local/docker-compose.yml</c>. Without the variables every test
/// keeps using a throwaway SQLite file and a temporary folder.
/// <list type="bullet">
/// <item><c>PURCHASE_MEMORY_TEST_POSTGRES</c> — admin connection string; each
/// test host gets its own freshly created database, dropped afterwards.</item>
/// <item><c>PURCHASE_MEMORY_TEST_S3_URL</c> — S3 endpoint; each host gets its
/// own key prefix in the <c>pm-tests</c> bucket. Keys default to the
/// throwaway dev pair from <c>deploy/local/s3-dev-identities.json</c>.</item>
/// </list>
/// </summary>
internal static class ExternalTestServices
{
    public const string PostgresVariable = "PURCHASE_MEMORY_TEST_POSTGRES";
    public const string S3UrlVariable = "PURCHASE_MEMORY_TEST_S3_URL";
    public const string TestBucket = "pm-tests";

    public static string? PostgresAdminConnectionString =>
        NullIfEmpty(Environment.GetEnvironmentVariable(PostgresVariable));

    public static S3StorageOptions? S3Options(string keyPrefix)
    {
        var url = NullIfEmpty(Environment.GetEnvironmentVariable(S3UrlVariable));
        if (url is null)
        {
            return null;
        }

        return new S3StorageOptions
        {
            ServiceUrl = url,
            Region = "us-east-1",
            Bucket = TestBucket,
            KeyPrefix = keyPrefix,
            AccessKeyId = NullIfEmpty(Environment.GetEnvironmentVariable("PURCHASE_MEMORY_TEST_S3_ACCESS_KEY"))
                ?? "pm-dev-access-key",
            SecretAccessKey = NullIfEmpty(Environment.GetEnvironmentVariable("PURCHASE_MEMORY_TEST_S3_SECRET_KEY"))
                ?? "pm-dev-secret-key-not-a-real-secret"
        };
    }

    public static async Task EnsureBucketAsync(S3StorageOptions options)
    {
        using var client = S3ReceiptBlobStorage.CreateClient(options);
        try
        {
            await client.PutBucketAsync(new PutBucketRequest { BucketName = options.Bucket });
        }
        catch (AmazonS3Exception exception) when (exception.ErrorCode is "BucketAlreadyOwnedByYou" or "BucketAlreadyExists")
        {
        }
    }

    public static async Task DeletePrefixAsync(S3StorageOptions options)
    {
        using var client = S3ReceiptBlobStorage.CreateClient(options);
        var request = new ListObjectsV2Request { BucketName = options.Bucket, Prefix = options.KeyPrefix + "/" };
        ListObjectsV2Response response;
        do
        {
            response = await client.ListObjectsV2Async(request);
            foreach (var entry in response.S3Objects ?? [])
            {
                await client.DeleteObjectAsync(options.Bucket, entry.Key);
            }

            request.ContinuationToken = response.NextContinuationToken;
        }
        while (response.IsTruncated is true);
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}

/// <summary>A database of its own on the opt-in Postgres server.</summary>
internal sealed class PostgresTestDatabase : IAsyncDisposable
{
    private readonly string _adminConnectionString;
    private readonly string _name;

    private PostgresTestDatabase(string adminConnectionString, string name, string connectionString)
    {
        _adminConnectionString = adminConnectionString;
        _name = name;
        ConnectionString = connectionString;
    }

    public string ConnectionString { get; }

    public static PostgresTestDatabase? TryCreate()
    {
        var admin = ExternalTestServices.PostgresAdminConnectionString;
        if (admin is null)
        {
            return null;
        }

        var name = "pm_test_" + Guid.NewGuid().ToString("N");
        using (var connection = new NpgsqlConnection(admin))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE \"{name}\"";
            command.ExecuteNonQuery();
        }

        var builder = new NpgsqlConnectionStringBuilder(admin) { Database = name };
        return new PostgresTestDatabase(admin, name, builder.ConnectionString);
    }

    public async ValueTask DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(_adminConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS \"{_name}\" WITH (FORCE)";
        await command.ExecuteNonQueryAsync();
    }
}
