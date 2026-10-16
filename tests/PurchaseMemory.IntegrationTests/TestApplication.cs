using Microsoft.Extensions.DependencyInjection;
using PurchaseMemory.Application.Products;
using PurchaseMemory.Application.Products.Categories;
using PurchaseMemory.Application.Purchases;
using PurchaseMemory.Infrastructure;
using PurchaseMemory.Infrastructure.Persistence;

namespace PurchaseMemory.IntegrationTests;

internal sealed class TestApplication : IAsyncDisposable
{
    private readonly string _temporaryDirectory;
    private readonly PostgresTestDatabase? _postgres;

    private TestApplication(ServiceProvider services, string temporaryDirectory, PostgresTestDatabase? postgres)
    {
        Services = services;
        _temporaryDirectory = temporaryDirectory;
        _postgres = postgres;
    }

    public ServiceProvider Services { get; }

    public static async Task<TestApplication> CreateAsync()
    {
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "purchase-memory-tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(temporaryDirectory);
        var databasePath = Path.Combine(temporaryDirectory, "test.db");

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero)));
        services.AddScoped<IProductMatcher, ProductMatcher>();
        services.AddScoped<IProductCatalogService, ProductCatalogService>();
        services.AddLogging();
        services.AddScoped<IProductManagementService, ProductManagementService>();
        services.AddScoped<IPurchaseDraftService, PurchaseDraftService>();
        services.AddScoped<IProductCategoryService, ProductCategoryService>();
        services.AddScoped<PurchaseMemory.Application.Products.Tags.IProductTagService, PurchaseMemory.Application.Products.Tags.ProductTagService>();
        services.AddScoped<PurchaseMemory.Application.ReviewQueue.IReviewQueueService, PurchaseMemory.Application.ReviewQueue.ReviewQueueService>();
        // PURCHASE_MEMORY_TEST_POSTGRES runs the same tests on real Postgres.
        var postgres = PostgresTestDatabase.TryCreate();
        services.AddPurchaseMemoryInfrastructure(postgres is null
            ? DatabaseSettings.Sqlite($"Data Source={databasePath}")
            : DatabaseSettings.Postgres(postgres.ConnectionString));

        var serviceProvider = services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });

        await serviceProvider.EnsureDatabaseMigratedAsync(TestContext.Current.CancellationToken);
        return new TestApplication(serviceProvider, temporaryDirectory, postgres);
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }

        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
