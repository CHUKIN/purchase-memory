namespace PurchaseMemory.Domain.Products;

/// <summary>
/// One shop barcode of a product. A code belongs to at most one product in
/// the household (a unique index); a product may carry several (new pack, new
/// size). Barcodes attach to the product now and can move to package editions
/// later without losing anything.
/// </summary>
public sealed class ProductBarcode
{
    /// <summary>More than this many codes on one product is a mistake, not a real pack history.</summary>
    public const int MaximumPerProduct = 20;

    private ProductBarcode()
    {
        // Required by Entity Framework Core.
    }

    public Guid Id { get; private set; }

    public Guid ProductId { get; private set; }

    /// <summary>Normalized code (<see cref="Barcode.TryNormalize"/>).</summary>
    public string Code { get; private set; } = string.Empty;

    public ProductBarcodeSource Source { get; private set; }

    /// <summary>User id of the household member who added it.</summary>
    public string CreatedBy { get; private set; } = string.Empty;

    public string? CreatedByEmail { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static ProductBarcode Create(
        Guid productId,
        string normalizedCode,
        ProductBarcodeSource source,
        string createdBy,
        string? createdByEmail,
        DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(createdBy);
        if (productId == Guid.Empty)
        {
            throw new ArgumentException("Product identifier cannot be empty.", nameof(productId));
        }

        if (normalizedCode is null || Barcode.TryNormalize(normalizedCode) != normalizedCode)
        {
            throw new ArgumentException("The barcode must be a normalized shop barcode.", nameof(normalizedCode));
        }

        if (!Enum.IsDefined(source))
        {
            throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown barcode source.");
        }

        return new ProductBarcode
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            Code = normalizedCode,
            Source = source,
            CreatedBy = Limit(createdBy.Trim(), 200)!,
            CreatedByEmail = Limit(createdByEmail?.Trim(), 320),
            CreatedAt = createdAt
        };
    }

    private static string? Limit(string? value, int length) => value is null || value.Length <= length ? value : value[..length];
}

public enum ProductBarcodeSource
{
    /// <summary>Read by the camera.</summary>
    Scan = 0,

    /// <summary>Typed by a person.</summary>
    Manual = 1,

    /// <summary>Scanned, and the product was created from the Open Food Facts name.</summary>
    OpenFoodFacts = 2
}
