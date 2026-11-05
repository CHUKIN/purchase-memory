using Microsoft.Extensions.Options;
using PurchaseMemory.Application.Impressions;
using PurchaseMemory.Application.Products.Media;
using PurchaseMemory.Application.Receipts;
using PurchaseMemory.Infrastructure.Images;
using PurchaseMemory.Infrastructure.Persistence;

namespace PurchaseMemory.Api.Configuration;

/// <summary>
/// Chooses the database and the photo storage from configuration. Defaults
/// (SQLite file + local folder under <c>Paths:DataDirectory</c>) keep local
/// development exactly as before; Supabase is opted into with
/// <c>Database__Provider=Postgres</c> and <c>Storage__Provider=S3</c>.
/// </summary>
public static class StorageSetup
{
    public static DatabaseSettings ResolveDatabase(IConfiguration configuration, RuntimePaths paths)
    {
        var provider = ParseEnum(configuration["Database:Provider"], DatabaseProvider.Sqlite, "Database:Provider");
        if (provider == DatabaseProvider.Sqlite)
        {
            return DatabaseSettings.Sqlite($"Data Source={paths.DatabaseFilePath}");
        }

        var connectionString = configuration.GetConnectionString("Postgres");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Database:Provider is Postgres, but ConnectionStrings:Postgres (env ConnectionStrings__Postgres) "
                + "is empty. See docs/ПЕРЕМЕННЫЕ_И_СЕКРЕТЫ.md.");
        }

        return DatabaseSettings.Postgres(connectionString);
    }

    public static IServiceCollection AddReceiptImageStorage(
        this IServiceCollection services,
        IConfiguration configuration,
        RuntimePaths paths)
    {
        services
            .AddOptions<ReceiptImageStorageOptions>()
            .Bind(configuration.GetSection(ReceiptImageStorageOptions.SectionName))
            .PostConfigure(options =>
            {
                options.RootDirectory = paths.ReceiptImageDirectory;
                options.MaximumFileSizeBytes = 15 * 1024 * 1024;
            })
            .Validate(
                options => options.AnalysisSource != ReceiptImagePurpose.Analysis
                    && options.DisplayCopy.MaximumLongSidePixels is >= 256 and <= 10_000
                    && options.DisplayCopy.JpegQuality is >= 30 and <= 100,
                "ReceiptImages: AnalysisSource must be Original or Viewing, DisplayCopy:MaximumLongSidePixels "
                + "256–10000 and DisplayCopy:JpegQuality 30–100.")
            .ValidateOnStart();

        var provider = ParseEnum(configuration["Storage:Provider"], ReceiptStorageProvider.Local, "Storage:Provider");
        if (provider == ReceiptStorageProvider.S3)
        {
            services.Configure<S3StorageOptions>(configuration.GetSection(S3StorageOptions.SectionName));
            services.AddSingleton<IReceiptBlobStorage, S3ReceiptBlobStorage>();
        }
        else
        {
            services.AddSingleton<IReceiptBlobStorage, LocalReceiptBlobStorage>();
        }

        services.AddSingleton<IReceiptImageStore, ReceiptImageStore>();
        services.AddSingleton<IImpressionPhotoStore, ImpressionPhotoStore>();
        // Product photos share the receipt bucket under «products/»: no new bucket or secret.
        services.AddSingleton<IProductPhotoStore, ProductPhotoStore>();
        return services;
    }

    public static string DescribeImageStorage(IConfiguration configuration, RuntimePaths paths)
    {
        var provider = ParseEnum(configuration["Storage:Provider"], ReceiptStorageProvider.Local, "Storage:Provider");
        return provider == ReceiptStorageProvider.S3
            ? $"S3 bucket {configuration["Storage:S3:Bucket"] ?? new S3StorageOptions().Bucket} at {configuration["Storage:S3:ServiceUrl"]}"
            : $"local folder {paths.ReceiptImageDirectory}";
    }

    private static TEnum ParseEnum<TEnum>(string? value, TEnum fallback, string key)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new InvalidOperationException(
                $"{key} '{value}' is not one of: {string.Join(", ", Enum.GetNames<TEnum>())}.");
    }
}
