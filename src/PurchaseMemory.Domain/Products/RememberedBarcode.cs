namespace PurchaseMemory.Domain.Products;

/// <summary>
/// A barcode scanned in the shop with «Запомнить на потом»: not tied to any
/// product yet. It waits in «Ревью» until a household member links it to a
/// product (which forgets it) or forgets it. The Open Food Facts name, if it
/// knew the code, is kept as a hint only.
/// </summary>
public sealed class RememberedBarcode
{
    /// <summary>The list is a to-do list, not an archive; the oldest drop off past this.</summary>
    public const int MaximumCount = 500;

    public const int MaximumHintLength = 200;

    public const int MaximumImageUrlLength = 500;

    private RememberedBarcode()
    {
        // Required by Entity Framework Core.
    }

    /// <summary>Normalized code; the key.</summary>
    public string Code { get; private set; } = string.Empty;

    public DateTimeOffset RememberedAt { get; private set; }

    /// <summary>User id of the member who remembered it last.</summary>
    public string RememberedBy { get; private set; } = string.Empty;

    public string? OffName { get; private set; }

    public string? OffBrand { get; private set; }

    public string? OffQuantity { get; private set; }

    /// <summary>Only an https image on Open Food Facts' own hosts; checked by the caller.</summary>
    public string? OffImageUrl { get; private set; }

    public static RememberedBarcode Create(string normalizedCode)
    {
        if (normalizedCode is null || Barcode.TryNormalize(normalizedCode) != normalizedCode)
        {
            throw new ArgumentException("The barcode must be a normalized shop barcode.", nameof(normalizedCode));
        }

        return new RememberedBarcode { Code = normalizedCode };
    }

    /// <summary>Remembering the same code again moves it to the top and replaces the hint.</summary>
    public void Remember(string rememberedBy, DateTimeOffset rememberedAt, string? name, string? brand, string? quantity, string? imageUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rememberedBy);
        RememberedBy = Limit(rememberedBy.Trim(), 200)!;
        RememberedAt = rememberedAt;
        OffName = Limit(Clean(name), MaximumHintLength);
        OffBrand = Limit(Clean(brand), MaximumHintLength);
        OffQuantity = Limit(Clean(quantity), MaximumHintLength);
        OffImageUrl = imageUrl is not null && imageUrl.Length <= MaximumImageUrlLength ? imageUrl : null;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Limit(string? value, int length) => value is null || value.Length <= length ? value : value[..length];
}
