using Microsoft.EntityFrameworkCore;
using PurchaseMemory.Domain.Products;
using PurchaseMemory.Domain.Purchases;
using PurchaseMemory.Infrastructure;
using PurchaseMemory.Infrastructure.Persistence;

namespace PurchaseMemory.IntegrationTests;

/// <summary>
/// Upgrading a database that still has lines waiting in pending matches: each
/// becomes linked to a flagged product (SQLite like the owner's local file,
/// and Postgres like production when <c>PURCHASE_MEMORY_TEST_POSTGRES</c> is set).
/// </summary>
public sealed class ProductReviewFlagMigrationTests
{
    private const string SqliteMigrationBefore = "20261014171920_AddPurchaseItemApprovals";
    private const string PostgresMigrationBefore = "20261014171932_AddPurchaseItemApprovals";

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PendingMatchesBecomeFlaggedProductsOnSqlite()
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
    public async Task PendingMatchesBecomeFlaggedProductsOnPostgres()
    {
        Assert.SkipWhen(
            ExternalTestServices.PostgresAdminConnectionString is null,
            $"Set {ExternalTestServices.PostgresVariable} to run against real Postgres.");
        await using var database = PostgresTestDatabase.TryCreate()!;
        var builder = new DbContextOptionsBuilder<PurchaseMemoryDbContext>();
        DependencyInjection.ConfigureDatabase(builder, DatabaseSettings.Postgres(database.ConnectionString));
        await RunScenarioAsync(builder.Options, PostgresMigrationBefore, postgres: true);
    }

    /// <summary>
    /// Two stores, a wording waiting twice in one store and once in the other
    /// (one flagged product for all three lines, with an alias per store), a
    /// wording whose alias appeared meanwhile (joins that product), and a
    /// line that was linked all along.
    /// </summary>
    private static async Task RunScenarioAsync(DbContextOptions<PurchaseMemoryDbContext> options, string before, bool postgres)
    {
        const long earlier = 1_785_758_400_000L;
        const long later = 1_785_844_800_000L;
        var gouda150 = Guid.NewGuid();
        var gouda500 = Guid.NewGuid();
        var firstPurchase = Guid.NewGuid();
        var secondPurchase = Guid.NewGuid();
        var lidlPurchase = Guid.NewGuid();
        var firstLine = Guid.NewGuid();
        var secondLine = Guid.NewGuid();
        var lidlLine = Guid.NewGuid();
        var lidlSameWordingLine = Guid.NewGuid();
        var linkedLine = Guid.NewGuid();
        var firstMatch = Guid.NewGuid();
        var secondMatch = Guid.NewGuid();
        var lidlMatch = Guid.NewGuid();
        var lidlSameWordingMatch = Guid.NewGuid();

        await using (var oldContext = new PurchaseMemoryDbContext(options))
        {
            await oldContext.Database.MigrateAsync(before, Cancel);
            async Task Run(string template, params object[] parameters) =>
                await oldContext.Database.ExecuteSqlRawAsync(Dialect(template, postgres), parameters, Cancel);

            await Run(
                "INSERT INTO [[products]] ([Id], [CanonicalName], [NormalizedName], [CreatedAt], [UpdatedAt]) VALUES ({0}, {1}, {2}, {3}, {3})",
                gouda150, "Ser Gouda plastry 150g", "ser gouda plastry 150 g", earlier);
            await Run(
                "INSERT INTO [[products]] ([Id], [CanonicalName], [NormalizedName], [CreatedAt], [UpdatedAt]) VALUES ({0}, {1}, {2}, {3}, {3})",
                gouda500, "Ser Gouda 500g", "ser gouda 500 g", earlier);
            await Run(
                "INSERT INTO [[product_aliases]] ([Id], [ProductId], [MerchantKey], [NormalizedText], [RawText], [Source], [CreatedAt]) VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6})",
                Guid.NewGuid(), gouda150, "biedronka", "ser gouda plastry 150 g", "Ser Gouda plastry 150g", "AutoCreated", earlier);
            await Run(
                "INSERT INTO [[product_aliases]] ([Id], [ProductId], [MerchantKey], [NormalizedText], [RawText], [Source], [CreatedAt]) VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6})",
                Guid.NewGuid(), gouda500, "lidl", "ser gouda 500 g", "SER GOUDA 500 G", "HumanConfirmed", later);

            foreach (var (purchaseId, merchant, createdAt) in new[]
                     {
                         (firstPurchase, "Biedronka", earlier), (secondPurchase, "Biedronka", later), (lidlPurchase, "Lidl", earlier)
                     })
            {
                await Run(
                    "INSERT INTO [[purchase_drafts]] ([Id], [CreatedAt], [UpdatedAt], [MerchantName], [ReceiptTotal]) VALUES ({0}, {1}, {1}, {2}, NULL)",
                    purchaseId, createdAt, merchant);
            }

            foreach (var (lineId, purchaseId, position, name, productId) in new (Guid, Guid, int, string, Guid?)[]
                     {
                         (firstLine, firstPurchase, 0, "Ser Gouda plastry 250g", null),
                         (linkedLine, firstPurchase, 1, "Ser Gouda plastry 150g", gouda150),
                         (secondLine, secondPurchase, 0, "Ser Gouda plastry 250g", null),
                         (lidlLine, lidlPurchase, 0, "SER GOUDA 500 G", null),
                         (lidlSameWordingLine, lidlPurchase, 1, "SER GOUDA PLASTRY 250G", null)
                     })
            {
                // A typed parameter for the link, a literal NULL for none: an untyped null upsets uuid columns.
                if (productId is { } linkedProductId)
                {
                    await Run(
                        "INSERT INTO [[purchase_items]] ([Id], [PurchaseDraftId], [Position], [Name], [Quantity], [UnitPrice], [LineTotal], [ProductId]) VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {5}, {6})",
                        lineId, purchaseId, position, name, 1m, 7.99m, linkedProductId);
                }
                else
                {
                    await Run(
                        "INSERT INTO [[purchase_items]] ([Id], [PurchaseDraftId], [Position], [Name], [Quantity], [UnitPrice], [LineTotal], [ProductId]) VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {5}, NULL)",
                        lineId, purchaseId, position, name, 1m, 7.99m);
                }
            }

            foreach (var (matchId, lineId, merchant, raw, normalized, raisedAt) in new[]
                     {
                         (firstMatch, firstLine, "biedronka", "Ser Gouda plastry 250g", "ser gouda plastry 250 g", earlier),
                         (secondMatch, secondLine, "biedronka", "Ser Gouda plastry 250g", "ser gouda plastry 250 g", later),
                         (lidlMatch, lidlLine, "lidl", "SER GOUDA 500 G", "ser gouda 500 g", earlier),
                         (lidlSameWordingMatch, lidlSameWordingLine, "lidl", "SER GOUDA PLASTRY 250G", "ser gouda plastry 250 g", later)
                     })
            {
                await Run(
                    "INSERT INTO [[pending_product_matches]] ([Id], [PurchaseItemId], [MerchantKey], [RawItemName], [NormalizedItemName], [RaisedAt]) VALUES ({0}, {1}, {2}, {3}, {4}, {5})",
                    matchId, lineId, merchant, raw, normalized, raisedAt);
            }

            foreach (var (matchId, position, productId, score) in new[]
                     {
                         (firstMatch, 0, gouda150, 0.80), (firstMatch, 1, gouda500, 0.65),
                         (secondMatch, 0, gouda150, 0.78), (lidlMatch, 0, gouda150, 0.70),
                         (lidlSameWordingMatch, 0, gouda500, 0.72), (lidlSameWordingMatch, 1, gouda150, 0.60)
                     })
            {
                await Run(
                    "INSERT INTO [[pending_product_match_candidates]] ([Id], [PendingProductMatchId], [Position], [ProductId], [Score]) VALUES ({0}, {1}, {2}, {3}, {4})",
                    Guid.NewGuid(), matchId, position, productId, score);
            }
        }

        await using var upgraded = new PurchaseMemoryDbContext(options);
        await upgraded.Database.MigrateAsync(Cancel);

        // One flagged product for the wording waiting in both stores, named after the earliest line.
        var flagged = await upgraded.Products.SingleAsync(product => product.NeedsReview, Cancel);
        Assert.Equal(("Ser Gouda plastry 250g", "ser gouda plastry 250 g"), (flagged.CanonicalName, flagged.NormalizedName));
        Assert.Equal((ProductReviewReason.AmbiguousMatch, ProductOrigin.Receipt), (flagged.ReviewReason, flagged.Origin));
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(earlier), flagged.CreatedAt);
        Assert.Equal(3, await upgraded.Products.CountAsync(Cancel));
        Assert.All(
            await upgraded.Products.Where(product => !product.NeedsReview).ToListAsync(Cancel),
            product => Assert.Equal((ProductOrigin.Receipt, (ProductReviewReason?)null), (product.Origin, product.ReviewReason)));

        // Every waiting line has a product; the one whose wording got an alias joined it.
        var lines = await upgraded.Set<PurchaseItem>().ToDictionaryAsync(item => item.Id, Cancel);
        Assert.Equal(flagged.Id, lines[firstLine].ProductId);
        Assert.Equal(flagged.Id, lines[secondLine].ProductId);
        Assert.Equal(flagged.Id, lines[lidlSameWordingLine].ProductId);
        Assert.Equal(gouda500, lines[lidlLine].ProductId);
        Assert.Equal(gouda150, lines[linkedLine].ProductId);

        // An alias per store the wording appeared in, so each store's next receipt hits it.
        var aliases = await upgraded.ProductAliases
            .Where(entry => entry.ProductId == flagged.Id)
            .OrderBy(entry => entry.MerchantKey)
            .ToListAsync(Cancel);
        Assert.Equal(
            [
                ("biedronka", "ser gouda plastry 250 g", "Ser Gouda plastry 250g", ProductAliasSource.AutoCreated),
                ("lidl", "ser gouda plastry 250 g", "SER GOUDA PLASTRY 250G", ProductAliasSource.AutoCreated)
            ],
            aliases.Select(alias => (alias.MerchantKey, alias.NormalizedText, alias.RawText, alias.Source)));

        // Candidates carried over from both stores: best score per candidate, best first.
        var candidates = await upgraded.ProductReviewCandidates
            .Where(candidate => candidate.ProductId == flagged.Id)
            .OrderBy(candidate => candidate.Position)
            .ToListAsync(Cancel);
        Assert.Equal([(0, gouda150, 0.80), (1, gouda500, 0.72)],
            candidates.Select(candidate => (candidate.Position, candidate.CandidateProductId, candidate.Score)));
        Assert.Equal(2, await upgraded.ProductReviewCandidates.CountAsync(Cancel));

        var audit = await upgraded.ProductAuditEntries.SingleAsync(entry => entry.ProductId == flagged.Id, Cancel);
        Assert.Equal((ProductAuditEntry.ReviewField, ProductAuditChangeSource.Matching, "system"), (audit.FieldName, audit.Source, audit.Actor));

        var pendingTables = postgres
            ? await upgraded.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*)::int AS \"Value\" FROM information_schema.tables WHERE table_schema = 'purchase_memory' AND table_name LIKE 'pending_product_match%'")
                .SingleAsync(Cancel)
            : await upgraded.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS \"Value\" FROM sqlite_master WHERE type = 'table' AND name LIKE 'pending_product_match%'")
                .SingleAsync(Cancel);
        Assert.Equal(0, pendingTables);
        Assert.False(upgraded.Database.HasPendingModelChanges());

        // The converted product behaves like one the matcher flags today.
        var product = await upgraded.Products.SingleAsync(entry => entry.Id == flagged.Id, Cancel);
        Assert.True(product.ClearReviewFlag(DateTimeOffset.UtcNow));
        await upgraded.SaveChangesAsync(Cancel);
    }

    /// <summary>[[table]] and [column] become the provider's quoting (Postgres: schema and double quotes).</summary>
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
