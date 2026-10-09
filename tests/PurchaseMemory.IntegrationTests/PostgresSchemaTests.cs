using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PurchaseMemory.Domain.Purchases;
using PurchaseMemory.Infrastructure.Persistence;

namespace PurchaseMemory.IntegrationTests;

/// <summary>
/// Postgres-only guarantees, run when <c>PURCHASE_MEMORY_TEST_POSTGRES</c> is set.
/// </summary>
public sealed class PostgresSchemaTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TablesLiveOutsideThePublicSchemaSupabaseExposes()
    {
        SkipWithoutPostgres();
        await using var application = await TestApplication.CreateAsync();
        await using var scope = application.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>();
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        await connection.OpenAsync(Cancel);

        var schemas = await QueryAsync(
            connection,
            "SELECT DISTINCT table_schema FROM information_schema.tables "
            + "WHERE table_schema NOT IN ('pg_catalog', 'information_schema')");
        Assert.Equal([PurchaseMemoryDbContext.PostgresSchema], schemas);

        var tables = await QueryAsync(
            connection,
            $"SELECT table_name FROM information_schema.tables WHERE table_schema = '{PurchaseMemoryDbContext.PostgresSchema}' ORDER BY 1");
        Assert.Contains("purchase_drafts", tables);
        Assert.Contains("receipt_captures", tables);
        Assert.Contains("product_tags", tables);
        Assert.Contains("product_tag_links", tables);
        Assert.Contains("analytics_questions", tables);
        Assert.Contains("__EFMigrationsHistory", tables);

        // Supabase's Data API roles; created here when the plain test server lacks them.
        foreach (var role in new[] { "anon", "authenticated" })
        {
            await ExecuteAsync(
                connection,
                $"DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{role}') THEN CREATE ROLE {role} NOLOGIN; END IF; END $$;");
            var usage = await QueryAsync(
                connection,
                $"SELECT has_schema_privilege('{role}', '{PurchaseMemoryDbContext.PostgresSchema}', 'USAGE')::text");
            Assert.Equal(["false"], usage);
        }
    }

    [Fact]
    public async Task MoneyQuantitiesAndMomentsRoundTripExactly()
    {
        SkipWithoutPostgres();
        await using var application = await TestApplication.CreateAsync();
        var purchasedAt = new DateTimeOffset(2026, 3, 29, 1, 30, 15, 123, TimeSpan.FromHours(1));
        Guid id;

        await using (var scope = application.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>();
            var draft = PurchaseDraft.Create(
                "Żabka Łódź",
                purchasedAt,
                "PLN",
                12345678.99m,
                [PurchaseItem.Create(0, "Ser gouda", 0.482m, "kg", 29.99m, 14.46m)],
                purchasedAt);
            id = draft.Id;
            context.PurchaseDrafts.Add(draft);
            await context.SaveChangesAsync(Cancel);
        }

        await using (var scope = application.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>();
            var stored = await context.PurchaseDrafts.Include(draft => draft.Items).SingleAsync(draft => draft.Id == id, Cancel);
            Assert.Equal("Żabka Łódź", stored.MerchantName);
            Assert.Equal(purchasedAt, stored.PurchasedAt);
            Assert.Equal(12345678.99m, stored.ReceiptTotal);
            var item = Assert.Single(stored.Items);
            Assert.Equal((0.482m, 29.99m, 14.46m), (item.Quantity, item.UnitPrice, item.LineTotal));
        }
    }

    private static void SkipWithoutPostgres() =>
        Assert.SkipWhen(
            ExternalTestServices.PostgresAdminConnectionString is null,
            $"Set {ExternalTestServices.PostgresVariable} to run against real Postgres.");

    private static async Task<List<string>> QueryAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(Cancel);
        var values = new List<string>();
        while (await reader.ReadAsync(Cancel))
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(Cancel);
    }
}

/// <summary>Runs on SQLite by default and on Postgres when opted in.</summary>
public sealed class DecimalRoundingTests
{
    [Fact]
    public async Task MoneyAndQuantitiesAreStoredRoundedIdenticallyOnEveryProvider()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var application = await TestApplication.CreateAsync();
        var now = new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);
        Guid id;
        await using (var scope = application.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>();
            var draft = PurchaseDraft.Create(
                "Test", now, "PLN", 1.995m,
                [PurchaseItem.Create(0, "Ser", 0.4825m, "kg", 2.345m, 1.005m)],
                now);
            id = draft.Id;
            context.PurchaseDrafts.Add(draft);
            await context.SaveChangesAsync(cancel);
        }

        await using (var scope = application.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>();
            var stored = await context.PurchaseDrafts.AsNoTracking().Include(draft => draft.Items)
                .SingleAsync(draft => draft.Id == id, cancel);
            Assert.Equal(2.00m, stored.ReceiptTotal);
            var item = Assert.Single(stored.Items);
            Assert.Equal((0.483m, 2.35m, 1.01m), (item.Quantity, item.UnitPrice, item.LineTotal));
        }
    }
}
