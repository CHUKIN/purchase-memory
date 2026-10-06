using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PurchaseMemory.Application.Products;
using PurchaseMemory.Domain.Purchases;
using PurchaseMemory.Infrastructure;
using PurchaseMemory.Infrastructure.Persistence;
using PurchaseMemory.ProductBackfill;

// Matches already saved purchases against the product catalog. The database path
// is always explicit: this tool must never be able to touch the owner's real
// database by default. Re-running it is safe — items that already carry a link
// are skipped, aliases are reused, and pending matches are replaced, not stacked.
const string ProductionOverrideFlag = "--allow-production-database";

var positional = args.Where(argument => !argument.StartsWith("--", StringComparison.Ordinal)).ToList();
var allowProduction = args.Contains(ProductionOverrideFlag, StringComparer.Ordinal);
var unknownFlag = args.FirstOrDefault(argument =>
    argument.StartsWith("--", StringComparison.Ordinal)
    && !string.Equals(argument, ProductionOverrideFlag, StringComparison.Ordinal));

if (positional.Count != 1 || string.IsNullOrWhiteSpace(positional[0]) || unknownFlag is not null)
{
    if (unknownFlag is not null)
    {
        Console.Error.WriteLine($"Unknown option: {unknownFlag}");
    }

    Console.Error.WriteLine(
        $"Usage: PurchaseMemory.ProductBackfill <path-to-sqlite-database> [{ProductionOverrideFlag}]");
    return 2;
}

var databasePath = Path.GetFullPath(positional[0]);
if (!File.Exists(databasePath))
{
    Console.Error.WriteLine($"Database not found: {databasePath}");
    return 2;
}

// The tool migrates and rewrites whatever it is handed, so the owner's live
// database is refused outright rather than merely «not the default». The check
// runs on the link-resolved path: a symlink is a pointer at the same bytes.
var realDatabasePath = ProductionDatabaseGuard.ResolveRealPath(databasePath);
var repositoryDataDirectory = ProductionDatabaseGuard.FindRepositoryDataDirectory(AppContext.BaseDirectory);
if (!allowProduction
    && ProductionDatabaseGuard.IsProductionDatabase(realDatabasePath, repositoryDataDirectory))
{
    Console.Error.WriteLine(
        $"Refusing to run against what looks like the live database: {realDatabasePath}");
    Console.Error.WriteLine(
        $"Copy it to a scratch location first, or pass {ProductionOverrideFlag} if you really mean it.");
    return 2;
}

Console.WriteLine($"Database: {databasePath}");

try
{
    return await RunAsync(databasePath);
}
catch (SqliteException exception)
{
    Console.Error.WriteLine($"Database error: {exception.Message}");
    return 1;
}
catch (DbUpdateException exception)
{
    Console.Error.WriteLine($"Could not write the catalog: {exception.Message}");
    return 1;
}
catch (InvalidOperationException exception)
{
    Console.Error.WriteLine($"Backfill failed: {exception.Message}");
    return 1;
}

static async Task<int> RunAsync(string databasePath)
{
    var services = new ServiceCollection();
    services.AddSingleton(TimeProvider.System);
    services.AddScoped<IProductMatcher, ProductMatcher>();
    services.AddPurchaseMemoryInfrastructure($"Data Source={databasePath}");

    await using var provider = services.BuildServiceProvider(
        new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

    await provider.EnsureDatabaseMigratedAsync();

    await using var scope = provider.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>();
    var matcher = scope.ServiceProvider.GetRequiredService<IProductMatcher>();

    Console.WriteLine($"before: {await ReadCountsAsync(dbContext)}");

    // Oldest first, so the product a receipt line bootstraps is the earliest
    // wording the household actually saw, and a later run cannot reorder the
    // catalog.
    var purchases = await dbContext.PurchaseDrafts
        .Include(purchase => purchase.Items)
        .OrderBy(purchase => purchase.CreatedAt)
        .ThenBy(purchase => purchase.Id)
        .ToListAsync();

    var summary = ProductMatchSummary.Empty;
    foreach (var purchase in purchases)
    {
        summary = summary.Merge(await matcher.MatchAsync(purchase, CancellationToken.None));
    }

    Console.WriteLine($"purchases processed: {purchases.Count}");
    Console.WriteLine(
        $"outcomes: aliasHit={summary.AliasHits} autoMatched={summary.AutoMatched} " +
        $"autoCreated={summary.AutoCreated} pending={summary.PendingReview} " +
        $"alreadyLinked={summary.AlreadyLinked} skipped={summary.Skipped}");
    Console.WriteLine($"after:  {await ReadCountsAsync(dbContext)}");
    return 0;
}

static async Task<CatalogCounts> ReadCountsAsync(PurchaseMemoryDbContext dbContext)
{
    return new CatalogCounts(
        await dbContext.Products.CountAsync(),
        await dbContext.ProductAliases.CountAsync(),
        await dbContext.Set<PurchaseItem>().CountAsync(item => item.ProductId != null),
        await dbContext.Set<PurchaseItem>().CountAsync(item => item.ProductId == null),
        await dbContext.PendingProductMatches.CountAsync());
}

internal sealed record CatalogCounts(
    int Products,
    int Aliases,
    int LinkedItems,
    int UnlinkedItems,
    int PendingMatches)
{
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"products={Products} aliases={Aliases} linkedItems={LinkedItems} unlinkedItems={UnlinkedItems} pendingMatches={PendingMatches}");
}
