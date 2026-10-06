namespace PurchaseMemory.Api.Configuration;

public sealed record RuntimePaths(
    string DataDirectory,
    string LogDirectory,
    string DatabaseFilePath,
    string LogFilePattern,
    string ReceiptImageDirectory)
{
    public static RuntimePaths Create(IHostEnvironment environment, IConfiguration configuration)
    {
        var dataDirectory = ResolveDirectory(
            environment.ContentRootPath,
            configuration["Paths:DataDirectory"] ?? "../../data");

        var logDirectory = ResolveDirectory(
            environment.ContentRootPath,
            configuration["Paths:LogDirectory"] ?? "../../logs");

        Directory.CreateDirectory(dataDirectory);
        Directory.CreateDirectory(logDirectory);
        var receiptImageDirectory = Path.Combine(dataDirectory, "receipts");
        Directory.CreateDirectory(receiptImageDirectory);

        return new RuntimePaths(
            dataDirectory,
            logDirectory,
            Path.Combine(dataDirectory, "purchases.db"),
            Path.Combine(logDirectory, "purchase-memory-.log"),
            receiptImageDirectory);
    }

    private static string ResolveDirectory(string contentRootPath, string configuredPath)
    {
        return Path.GetFullPath(configuredPath, contentRootPath);
    }
}
