namespace PurchaseMemory.Domain.Products;

/// <summary>
/// Canonical catalog product: identity, display name, one main category and an
/// optional brand. The category belongs to the product rather than to any
/// receipt line, so a correction automatically covers every past and future
/// purchase of it. Package editions and tags belong to later stages.
/// Every product-kind receipt line is linked to a product; a doubtful match
/// becomes a product of its own carrying <see cref="NeedsReview"/>.
/// </summary>
public sealed class Product
{
    public const int MaximumNameLength = 500;
    public const int MaximumBrandLength = 100;

    /// <summary>Longest human name (Polish or Russian); longer suggestions are cut.</summary>
    public const int MaximumDisplayNameLength = 200;

    private Product()
    {
        // Required by Entity Framework Core.
    }

    private Product(Guid id, string canonicalName, string normalizedName, ProductOrigin origin, DateTimeOffset createdAt)
    {
        Id = id;
        CanonicalName = canonicalName;
        NormalizedName = normalizedName;
        Origin = origin;
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
    /// Human Polish name («Kefir naturalny Activia 280 g»), as on the shelf.
    /// <c>null</c> when nobody has given one; the UI then shows
    /// <see cref="CanonicalName"/>, the receipt text, which stays unchanged.
    /// </summary>
    public string? NamePl { get; private set; }

    /// <summary><c>ProductNameNormalizer</c> key of <see cref="NamePl"/> for search.</summary>
    public string? NormalizedNamePl { get; private set; }

    /// <summary>
    /// <c>null</c> while nobody decided; a confirmed <c>null</c> name is a
    /// deliberate «just show the receipt text» that the AI leaves alone.
    /// </summary>
    public ProductAttributeSource? NamePlSource { get; private set; }

    /// <summary>Russian translation of the Polish name («Кефир натуральный Activia, 280 г»).</summary>
    public string? NameRu { get; private set; }

    /// <summary><c>ProductNameNormalizer</c> key of <see cref="NameRu"/> for search.</summary>
    public string? NormalizedNameRu { get; private set; }

    public ProductAttributeSource? NameRuSource { get; private set; }

    /// <summary>
    /// When the model last answered about this product's names, whatever it
    /// said. Like <see cref="CategorizationAttemptedAt"/>, it keeps a product
    /// the model could not name from being paid for again on every backfill.
    /// </summary>
    public DateTimeOffset? NamingAttemptedAt { get; private set; }

    /// <summary>
    /// When the categorizer last answered for this product, whatever it said.
    /// Products the model could not place are not re-sent on every save; only
    /// an explicit backfill retry asks again.
    /// </summary>
    public DateTimeOffset? CategorizationAttemptedAt { get; private set; }

    public ProductOrigin Origin { get; private set; }

    /// <summary>
    /// The «проверить» flag: the product exists so that its receipt line is
    /// not left without one, but a person still has to say whether it is one
    /// of its candidates (merge) or really a product of its own.
    /// </summary>
    public bool NeedsReview { get; private set; }

    /// <summary>Why <see cref="NeedsReview"/> is set; <c>null</c> when it is not.</summary>
    public ProductReviewReason? ReviewReason { get; private set; }

    /// <summary>
    /// «Больше не покупать»: when a household member marked the product as one
    /// not to buy again (shown red in the shop scan); <c>null</c> when not marked.
    /// </summary>
    public DateTimeOffset? AvoidSince { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Product Create(
        string canonicalName,
        string normalizedName,
        DateTimeOffset createdAt,
        ProductOrigin origin = ProductOrigin.Receipt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalName);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedName);

        if (!Enum.IsDefined(origin))
        {
            throw new ArgumentOutOfRangeException(nameof(origin), origin, "Unknown product origin.");
        }

        return new Product(
            Guid.NewGuid(),
            Truncate(canonicalName.Trim()),
            Truncate(normalizedName),
            origin,
            createdAt);
    }

    public void FlagForReview(ProductReviewReason reason, DateTimeOffset updatedAt)
    {
        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown review reason.");
        }

        NeedsReview = true;
        ReviewReason = reason;
        UpdatedAt = updatedAt;
    }

    /// <summary>A person settled the doubt. Returns whether the flag was set.</summary>
    public bool ClearReviewFlag(DateTimeOffset updatedAt)
    {
        if (!NeedsReview)
        {
            return false;
        }

        NeedsReview = false;
        ReviewReason = null;
        UpdatedAt = updatedAt;
        return true;
    }

    /// <summary>
    /// Takes over what a merged-away product knew about category and brand,
    /// each attribute on its own: this product's confirmed value wins, then
    /// the other product's confirmed value; otherwise this product keeps what
    /// it has (a suggestion or nothing), since a suggestion is only a guess.
    /// Returns which attributes changed.
    /// </summary>
    public (bool CategoryChanged, bool BrandChanged) AbsorbClassificationFrom(Product source, DateTimeOffset updatedAt)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Id == Id)
        {
            throw new ArgumentException("A product cannot absorb itself.", nameof(source));
        }

        var categoryChanged = false;
        if (CategorySource != ProductAttributeSource.Confirmed && source.CategorySource == ProductAttributeSource.Confirmed)
        {
            categoryChanged = CategoryId != source.CategoryId || CategorySource != source.CategorySource;
            CategoryId = source.CategoryId;
            CategorySource = ProductAttributeSource.Confirmed;
        }

        var brandChanged = false;
        if (BrandSource != ProductAttributeSource.Confirmed && source.BrandSource == ProductAttributeSource.Confirmed)
        {
            brandChanged = Brand != source.Brand || BrandSource != source.BrandSource;
            Brand = source.Brand;
            NormalizedBrand = source.NormalizedBrand;
            BrandSource = ProductAttributeSource.Confirmed;
        }

        if (categoryChanged || brandChanged)
        {
            UpdatedAt = updatedAt;
        }

        return (categoryChanged, brandChanged);
    }

    /// <summary>
    /// Takes over the merged-away product's confirmed human names, each
    /// language on its own, unless this product's name is confirmed already.
    /// Returns which names changed.
    /// </summary>
    public (bool PolishChanged, bool RussianChanged) AbsorbNamesFrom(Product source, DateTimeOffset updatedAt)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Id == Id)
        {
            throw new ArgumentException("A product cannot absorb itself.", nameof(source));
        }

        var polish = false;
        if (NamePlSource != ProductAttributeSource.Confirmed && source.NamePlSource == ProductAttributeSource.Confirmed)
        {
            polish = NamePl != source.NamePl || NamePlSource != source.NamePlSource;
            NamePl = source.NamePl;
            NormalizedNamePl = source.NormalizedNamePl;
            NamePlSource = ProductAttributeSource.Confirmed;
        }

        var russian = false;
        if (NameRuSource != ProductAttributeSource.Confirmed && source.NameRuSource == ProductAttributeSource.Confirmed)
        {
            russian = NameRu != source.NameRu || NameRuSource != source.NameRuSource;
            NameRu = source.NameRu;
            NormalizedNameRu = source.NormalizedNameRu;
            NameRuSource = ProductAttributeSource.Confirmed;
        }

        if (polish || russian)
        {
            UpdatedAt = updatedAt;
        }

        return (polish, russian);
    }

    /// <summary>
    /// Sets or clears «Больше не покупать». Marking an already marked product
    /// keeps the original date. Returns whether anything changed.
    /// </summary>
    public bool SetAvoid(bool avoid, DateTimeOffset since, DateTimeOffset updatedAt)
    {
        if (avoid == AvoidSince.HasValue)
        {
            return false;
        }

        AvoidSince = avoid ? since : null;
        UpdatedAt = updatedAt;
        return true;
    }

    /// <summary>
    /// A mark on the merged-away product marks this one too; with marks on
    /// both, the earlier date stays. Returns whether this product changed.
    /// </summary>
    public bool AbsorbAvoidFrom(Product source, DateTimeOffset updatedAt)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.AvoidSince is not { } sourceSince || (AvoidSince is { } own && own <= sourceSince))
        {
            return false;
        }

        AvoidSince = sourceSince;
        UpdatedAt = updatedAt;
        return true;
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
    /// A person set, corrected or cleared one human name. An empty name is a
    /// confirmed «no name»: the receipt text is shown and the AI stays away.
    /// </summary>
    public void ConfirmName(ProductNameLanguage language, string? name, string? normalizedName, DateTimeOffset updatedAt)
    {
        var clean = CleanDisplayName(name);
        var normalized = clean is null ? null : LimitDisplayName(normalizedName);
        switch (language)
        {
            case ProductNameLanguage.Polish:
                NamePl = clean;
                NormalizedNamePl = normalized;
                NamePlSource = ProductAttributeSource.Confirmed;
                break;
            case ProductNameLanguage.Russian:
                NameRu = clean;
                NormalizedNameRu = normalized;
                NameRuSource = ProductAttributeSource.Confirmed;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(language), language, "Unknown name language.");
        }

        UpdatedAt = updatedAt;
    }

    /// <summary>
    /// «Вернуть» after a name read from a photo: puts back what the name was
    /// before — a suggestion, or nothing (the receipt text shows). A person's
    /// name is never the «before» here, since a photo never replaces it.
    /// </summary>
    public void RestoreSuggestedName(
        ProductNameLanguage language,
        string? name,
        string? normalizedName,
        DateTimeOffset updatedAt)
    {
        var clean = CleanDisplayName(name);
        var normalized = clean is null ? null : LimitDisplayName(normalizedName);
        var source = clean is null ? (ProductAttributeSource?)null : ProductAttributeSource.Suggested;
        switch (language)
        {
            case ProductNameLanguage.Polish:
                NamePl = clean;
                NormalizedNamePl = normalized;
                NamePlSource = source;
                break;
            case ProductNameLanguage.Russian:
                NameRu = clean;
                NormalizedNameRu = normalized;
                NameRuSource = source;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(language), language, "Unknown name language.");
        }

        UpdatedAt = updatedAt;
    }

    /// <summary>
    /// «Верно»: every suggested name that has a value becomes confirmed as is.
    /// Returns the languages that changed.
    /// </summary>
    public IReadOnlyList<ProductNameLanguage> ConfirmSuggestedNames(DateTimeOffset updatedAt)
    {
        var confirmed = new List<ProductNameLanguage>(2);
        if (NamePlSource == ProductAttributeSource.Suggested && NamePl is not null)
        {
            NamePlSource = ProductAttributeSource.Confirmed;
            confirmed.Add(ProductNameLanguage.Polish);
        }

        if (NameRuSource == ProductAttributeSource.Suggested && NameRu is not null)
        {
            NameRuSource = ProductAttributeSource.Confirmed;
            confirmed.Add(ProductNameLanguage.Russian);
        }

        if (confirmed.Count > 0)
        {
            UpdatedAt = updatedAt;
        }

        return confirmed;
    }

    /// <summary>The name in <paramref name="language"/> and who decided it.</summary>
    public (string? Name, ProductAttributeSource? Source) NameIn(ProductNameLanguage language) => language switch
    {
        ProductNameLanguage.Polish => (NamePl, NamePlSource),
        ProductNameLanguage.Russian => (NameRu, NameRuSource),
        _ => throw new ArgumentOutOfRangeException(nameof(language), language, "Unknown name language.")
    };

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

    /// <summary>Trimmed, whitespace-collapsed and length-limited human name, or <c>null</c>.</summary>
    public static string? CleanDisplayName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var trimmed = string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return trimmed.Length <= MaximumDisplayNameLength ? trimmed : trimmed[..MaximumDisplayNameLength].TrimEnd();
    }

    public static string? LimitDisplayName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Length <= MaximumDisplayNameLength ? value : value[..MaximumDisplayNameLength];
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
