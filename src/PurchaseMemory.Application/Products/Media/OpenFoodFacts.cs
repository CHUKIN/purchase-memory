namespace PurchaseMemory.Application.Products.Media;

/// <summary>What Open Food Facts said about a barcode; a hint only, never trusted.</summary>
public sealed record OpenFoodFactsProduct(string? Name, string? Brand, string? Quantity, string? ImageUrl)
{
    /// <summary>
    /// Trims the texts and keeps the image only when it is an https picture on
    /// Open Food Facts' own hosts: the URL ends up in an <c>img</c> of every
    /// household member, so nothing else may get there.
    /// </summary>
    public static OpenFoodFactsProduct? Clean(string? name, string? brand, string? quantity, string? imageUrl)
    {
        var product = new OpenFoodFactsProduct(Text(name), Text(brand), Text(quantity), SafeImageUrl(imageUrl));
        return product.Name is null && product.Brand is null ? null : product;
    }

    public static string? SafeImageUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 500
            || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !uri.IsDefaultPort)
        {
            return null;
        }

        var host = uri.IdnHost;
        return host == "openfoodfacts.org" || host.EndsWith(".openfoodfacts.org", StringComparison.Ordinal)
            ? uri.AbsoluteUri
            : null;
    }

    private static string? Text(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var clean = value.Trim();
        return clean.Length <= 200 ? clean : clean[..200];
    }
}

public enum OpenFoodFactsStatus
{
    Found,
    NotFound,

    /// <summary>Timeout, network or server error: «the database is not answering».</summary>
    Error
}

public sealed record OpenFoodFactsLookup(OpenFoodFactsStatus Status, OpenFoodFactsProduct? Product)
{
    public static OpenFoodFactsLookup NotFound { get; } = new(OpenFoodFactsStatus.NotFound, null);

    public static OpenFoodFactsLookup Error { get; } = new(OpenFoodFactsStatus.Error, null);
}

/// <summary>
/// Open Food Facts product lookup by barcode. Only the barcode leaves the
/// server; no key is needed. Implementations answer quickly (a slow answer in
/// the shop is no answer), cache what they learn and never throw for a
/// provider problem.
/// </summary>
public interface IOpenFoodFactsClient
{
    /// <param name="code">A normalized barcode.</param>
    Task<OpenFoodFactsLookup> LookupAsync(string code, CancellationToken cancellationToken);
}
