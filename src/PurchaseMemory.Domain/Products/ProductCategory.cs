namespace PurchaseMemory.Domain.Products;

/// <summary>
/// One node of the household category tree. The tree is deliberately two
/// levels deep: a top-level group («Молочное и яйца») and its subcategories
/// («Сыр»). Seeded nodes carry a stable <see cref="SystemKey"/> so the UI can
/// translate them; renaming a node makes it the household's own and drops the
/// key, because a translated label would otherwise hide the new name.
/// </summary>
public sealed class ProductCategory
{
    public const int MaximumNameLength = 100;
    public const int MaximumSystemKeyLength = 60;

    private ProductCategory()
    {
        // Required by Entity Framework Core.
    }

    private ProductCategory(
        Guid id,
        Guid? parentId,
        string name,
        string? systemKey,
        int position,
        DateTimeOffset createdAt)
    {
        Id = id;
        ParentId = parentId;
        Name = name;
        SystemKey = systemKey;
        Position = position;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    /// <summary><c>null</c> for a top-level group.</summary>
    public Guid? ParentId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>Stable key of a seeded category; <c>null</c> for the household's own.</summary>
    public string? SystemKey { get; private set; }

    /// <summary>Display order among siblings.</summary>
    public int Position { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static ProductCategory Create(Guid? parentId, string name, int position, DateTimeOffset createdAt)
    {
        return new ProductCategory(Guid.NewGuid(), parentId, NormalizeName(name), null, position, createdAt);
    }

    /// <summary>Used only by the seed data, which needs fixed identifiers.</summary>
    public static ProductCategory Seed(
        Guid id,
        Guid? parentId,
        string name,
        string systemKey,
        int position,
        DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemKey);
        return new ProductCategory(id, parentId, NormalizeName(name), systemKey, position, createdAt);
    }

    public void Rename(string name, DateTimeOffset updatedAt)
    {
        var normalized = NormalizeName(name);
        if (normalized == Name)
        {
            return;
        }

        Name = normalized;
        SystemKey = null;
        UpdatedAt = updatedAt;
    }

    public static string NormalizeName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var trimmed = string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (trimmed.Length > MaximumNameLength)
        {
            throw new ArgumentException(
                $"Category name cannot exceed {MaximumNameLength} characters.",
                nameof(name));
        }

        return trimmed;
    }
}
