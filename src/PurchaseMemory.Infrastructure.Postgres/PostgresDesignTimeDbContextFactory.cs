using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using PurchaseMemory.Infrastructure;
using PurchaseMemory.Infrastructure.Persistence;

namespace PurchaseMemory.Infrastructure.Postgres;

/// <summary>
/// Used only by <c>dotnet ef migrations add</c>, which never connects. The
/// placeholder keeps design-time tooling away from every real database; to
/// run <c>dotnet ef database update</c> against a local container, pass
/// <c>--connection</c> explicitly.
/// </summary>
public sealed class PostgresDesignTimeDbContextFactory : IDesignTimeDbContextFactory<PurchaseMemoryDbContext>
{
    public PurchaseMemoryDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PurchaseMemoryDbContext>();
        DependencyInjection.ConfigureDatabase(
            options,
            DatabaseSettings.Postgres("Host=design-time.invalid;Database=purchase_memory"));
        return new PurchaseMemoryDbContext(options.Options);
    }
}
