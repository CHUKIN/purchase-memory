namespace PurchaseMemory.Domain.Products;

/// <summary>
/// A household tag such as «для ребёнка» or «к празднику». Unlike the one main
/// category, a product may carry any number of tags, so tags describe
/// overlapping, personal slices of the catalog. Tags are created only by a
/// person; nothing in the app invents them.
/// </summary>
public sealed class ProductTag
{
    public const int MaximumNameLength = 40;

    /// <summary>
    /// Room for the normalized key, which can outgrow the name: a boundary
    /// space between digit and letter runs and «ß» → «ss» add characters.
    /// </summary>
    public const int MaximumNormalizedNameLength = 120;

    private ProductTag()
    {
        // Required by Entity Framework Core.
    }

    private ProductTag(Guid id, string name, string normalizedName, DateTimeOffset createdAt)
    {
        Id = id;
        Name = name;
        NormalizedName = normalizedName;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    /// <summary>The name exactly as the person typed it, trimmed and with single spaces.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// Comparison key (case and Polish diacritics folded) that keeps «Акция» and
    /// «акция» one tag. Computed by the caller with <c>ProductNameNormalizer</c>
    /// and stored, so uniqueness does not depend on a database collation.
    /// </summary>
    public string NormalizedName { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static ProductTag Create(string name, string normalizedName, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedName);
        return new ProductTag(Guid.NewGuid(), CleanName(name), normalizedName, createdAt);
    }

    public void Rename(string name, string normalizedName, DateTimeOffset updatedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedName);
        var clean = CleanName(name);
        if (clean == Name)
        {
            return;
        }

        Name = clean;
        NormalizedName = normalizedName;
        UpdatedAt = updatedAt;
    }

    /// <summary>Trimmed, whitespace-collapsed name; too long is refused rather than cut.</summary>
    public static string CleanName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Tag name is required.", nameof(name));
        }

        var clean = string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (clean.Length > MaximumNameLength)
        {
            throw new ArgumentException($"Tag name cannot exceed {MaximumNameLength} characters.", nameof(name));
        }

        return clean;
    }
}

/// <summary>One product carrying one tag.</summary>
public sealed class ProductTagLink
{
    /// <summary>Keeps a product card readable and a filter meaningful.</summary>
    public const int MaximumTagsPerProduct = 20;

    private ProductTagLink()
    {
        // Required by Entity Framework Core.
    }

    private ProductTagLink(Guid productId, Guid tagId, DateTimeOffset createdAt)
    {
        ProductId = productId;
        TagId = tagId;
        CreatedAt = createdAt;
    }

    public Guid ProductId { get; private set; }

    public Guid TagId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static ProductTagLink Create(Guid productId, Guid tagId, DateTimeOffset createdAt)
    {
        if (productId == Guid.Empty || tagId == Guid.Empty)
        {
            throw new ArgumentException("Product and tag identifiers are required.");
        }

        return new ProductTagLink(productId, tagId, createdAt);
    }
}
