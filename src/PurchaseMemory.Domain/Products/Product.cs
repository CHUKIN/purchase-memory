namespace PurchaseMemory.Domain.Products;

/// <summary>
/// Canonical catalog product: identity, display name, one main category and an
/// optional brand. The category belongs to the product rather than to any
/// receipt line, so a correction automatically covers every past and future
/// purchase of it. Package editions and tags belong to later stages.
/// </summary>
public sealed class Product
{
    public const int MaximumNameLength = 500;
    public const int MaximumBrandLength = 100;

    private Product()
    {
        // Required by Entity Framework Core.
    }

    private Product(Guid id, string canonicalName, string normalizedName, DateTimeOffset createdAt)
    {
        Id = id;
        CanonicalName = canonicalName;
        NormalizedName = normalizedName;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    /// <summary>
    /// Human readable name as it should be shown in the catalog. The first
    /// receipt line that created the product supplies it verbatim.
    /// </summary>
    public string CanonicalName { get; private set; } = string.Empty;

    /// <summary>
    /// Result of <c>ProductNameNormalizer</c> applied to <see cref="CanonicalName"/>.
    /// Stored so lookups and uniqueness do not depend on SQLite collations.
    /// </summary>
    public string NormalizedName { get; private set; } = string.Empty;

    /// <summary><c>null</c> means «Без категории».</summary>
    public Guid? CategoryId { get; private set; }

    /// <summary>
    /// <c>null</c> while nobody — neither a person nor the categorizer — has
    /// decided anything; such products are the ones a suggestion run picks up.
    /// A confirmed <c>null</c> category is a deliberate «no category».
    /// </summary>
    public ProductAttributeSource? CategorySource { get; private set; }

    public string? Brand { get; private set; }

    /// <summary><c>ProductNameNormalizer</c> key of <see cref="Brand"/> for filtering.</summary>
    public string? NormalizedBrand { get; private set; }

    public ProductAttributeSource? BrandSource { get; private set; }

    /// <summary>
    /// When the categorizer last answered for this product, whatever it said.
    /// Products the model could not place are not re-sent on every save; only
    /// an explicit backfill retry asks again.
    /// </summary>
    public DateTimeOffset? CategorizationAttemptedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Product Create(string canonicalName, string normalizedName, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalName);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedName);

        return new Product(
            Guid.NewGuid(),
            Truncate(canonicalName.Trim()),
            Truncate(normalizedName),
            createdAt);
    }

    public void Rename(string canonicalName, string normalizedName, DateTimeOffset updatedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalName);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedName);

        CanonicalName = Truncate(canonicalName.Trim());
        NormalizedName = Truncate(normalizedName);
        UpdatedAt = updatedAt;
    }

    /// <summary>
    /// Applies an automatic suggestion unless a person already decided. Returns
    /// whether anything changed. Brand and category are guarded separately: a
    /// confirmed brand survives a suggestion that still fills in the category.
    /// </summary>
    public bool ApplySuggestion(
        Guid? categoryId,
        string? brand,
        string? normalizedBrand,
        DateTimeOffset updatedAt)
    {
        var changed = false;
        if (CategorySource != ProductAttributeSource.Confirmed && categoryId is not null && categoryId != CategoryId)
        {
            CategoryId = categoryId;
            CategorySource = ProductAttributeSource.Suggested;
            changed = true;
        }

        var cleanBrand = CleanBrand(brand);
        CategorizationAttemptedAt = updatedAt;
        if (BrandSource != ProductAttributeSource.Confirmed && cleanBrand is not null && cleanBrand != Brand)
        {
            Brand = cleanBrand;
            NormalizedBrand = LimitBrand(normalizedBrand);
            BrandSource = ProductAttributeSource.Suggested;
            changed = true;
        }

        if (changed)
        {
            UpdatedAt = updatedAt;
        }

        return changed;
    }

    /// <summary>A person chose (or confirmed) the category; <c>null</c> is «no category».</summary>
    public void ConfirmCategory(Guid? categoryId, DateTimeOffset updatedAt)
    {
        CategoryId = categoryId;
        CategorySource = ProductAttributeSource.Confirmed;
        UpdatedAt = updatedAt;
    }

    /// <summary>A person set, confirmed or cleared the brand.</summary>
    public void ConfirmBrand(string? brand, string? normalizedBrand, DateTimeOffset updatedAt)
    {
        Brand = CleanBrand(brand);
        NormalizedBrand = Brand is null ? null : LimitBrand(normalizedBrand);
        BrandSource = ProductAttributeSource.Confirmed;
        UpdatedAt = updatedAt;
    }

    /// <summary>
    /// The product's category was deleted. It moves to the parent category and
    /// keeps its source, because «this is dairy» is still what the person meant.
    /// With no parent it becomes uncategorized and eligible for a new
    /// suggestion again, since nobody chose «no category».
    /// </summary>
    public void MoveOutOfDeletedCategory(Guid? parentCategoryId, DateTimeOffset updatedAt)
    {
        CategoryId = parentCategoryId;
        if (parentCategoryId is null)
        {
            CategorySource = null;
        }

        UpdatedAt = updatedAt;
    }

    /// <summary>Trimmed, whitespace-collapsed and length-limited brand, or <c>null</c>.</summary>
    public static string? CleanBrand(string? brand)
    {
        if (string.IsNullOrWhiteSpace(brand))
        {
            return null;
        }

        var trimmed = string.Join(' ', brand.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return trimmed.Length <= MaximumBrandLength ? trimmed : trimmed[..MaximumBrandLength];
    }

    public static string? LimitBrand(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Length <= MaximumBrandLength ? value : value[..MaximumBrandLength];
    }

    /// <summary>
    /// Over-long names are cut, never rejected. A receipt line is legal up to its
    /// own 500-character limit, and normalization can nearly double that length
    /// by splitting digit runs from letter runs — refusing here would turn a
    /// legal purchase into a failed save. <c>ProductAlias</c> truncates for the
    /// same reason, so both sides of a link stay consistent.
    /// </summary>
    private static string Truncate(string value)
    {
        return value.Length <= MaximumNameLength ? value : value[..MaximumNameLength];
    }
}
