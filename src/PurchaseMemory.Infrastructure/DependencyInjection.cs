using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PurchaseMemory.Application.Analytics;
using PurchaseMemory.Application.Impressions;
using PurchaseMemory.Application.Products;
using PurchaseMemory.Application.Products.Categories;
using PurchaseMemory.Application.Purchases;
using PurchaseMemory.Application.Receipts;
using PurchaseMemory.Application.ReviewQueue;
using PurchaseMemory.Infrastructure.Analytics;
using PurchaseMemory.Infrastructure.Persistence;

namespace PurchaseMemory.Infrastructure;

public static class DependencyInjection
{
    /// <summary>SQLite at the given connection string (tools and tests).</summary>
    public static IServiceCollection AddPurchaseMemoryInfrastructure(
        this IServiceCollection services,
        string connectionString)
    {
        return services.AddPurchaseMemoryInfrastructure(DatabaseSettings.Sqlite(connectionString));
    }

    public static IServiceCollection AddPurchaseMemoryInfrastructure(
        this IServiceCollection services,
        DatabaseSettings database)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(database.ConnectionString);

        services.AddDbContext<PurchaseMemoryDbContext>(options => ConfigureDatabase(options, database));

        // The repositories are provider-neutral EF Core code despite their
        // historical "Sqlite" names; both providers run the same queries.
        services.AddScoped<IPurchaseDraftRepository, SqlitePurchaseDraftRepository>();
        services.AddScoped<IProductCatalogRepository, SqliteProductCatalogRepository>();
        services.AddScoped<IProductMaintenanceRepository, ProductMaintenanceRepository>();
        services.AddScoped<IReceiptCaptureRepository, SqliteReceiptCaptureRepository>();
        services.AddScoped<IProductCategoryRepository, ProductCategoryRepository>();
        services.AddScoped<PurchaseMemory.Application.Products.Tags.IProductTagRepository, ProductTagRepository>();
        services.AddScoped<IAnalyticsRepository, AnalyticsRepository>();
        services.AddScoped<IAnalyticsHistoryRepository, AnalyticsHistoryRepository>();
        services.AddScoped<IReviewQueueRepository, ReviewQueue.ReviewQueueRepository>();
        services.AddScoped<IProductImpressionRepository, ProductImpressionRepository>();

        // The API replaces this with a background worker; tools and tests keep
        // saving purchases without ever calling a model.
        services.TryAddSingleton<IProductCategorizationDispatcher, NullProductCategorizationDispatcher>();
        return services;
    }

    public static void ConfigureDatabase(DbContextOptionsBuilder options, DatabaseSettings database)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(database);

        switch (database.Provider)
        {
            case DatabaseProvider.Sqlite:
                options.UseSqlite(database.ConnectionString);
                break;
            case DatabaseProvider.Postgres:
                options.UseNpgsql(
                    database.ConnectionString,
                    npgsql => npgsql
                        .MigrationsAssembly(DatabaseSettings.PostgresMigrationsAssembly)
                        .MigrationsHistoryTable("__EFMigrationsHistory", PurchaseMemoryDbContext.PostgresSchema));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(database), database.Provider, "Unknown database provider.");
        }
    }
}
