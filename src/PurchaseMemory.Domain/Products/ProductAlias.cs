namespace PurchaseMemory.Domain.Products;

/// <summary>
/// Store-scoped link «merchant + normalized receipt text → product». Receipt
/// wording is stable inside one chain, so an alias turns every later occurrence
/// of the same line into a single indexed lookup instead of a similarity scan.
/// </summary>
public sealed class ProductAlias
{
    public const int MaximumTextLength = 500;

    /// <summary>
    /// Merchant key used when the receipt has no recognized merchant. A sentinel
    /// keeps the unique index usable, because SQLite treats NULLs as distinct.
    /// </summary>
    public const string UnknownMerchantKey = "";

    private ProductAlias()
    {
        // Required by Entity Framework Core.
    }

    private ProductAlias(
        Guid id,
        string merchantKey,
        string normalizedText,
        string rawText,
        Guid productId,
        ProductAliasSource source,
        DateTimeOffset createdAt)
    {
        Id = id;
        MerchantKey = merchantKey;
        NormalizedText = normalizedText;
        RawText = rawText;
        ProductId = productId;
        Source = source;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    /// <summary>Normalized merchant name, or <see cref="UnknownMerchantKey"/>.</summary>
    public string MerchantKey { get; private set; } = UnknownMerchantKey;

    public string NormalizedText { get; private set; } = string.Empty;

    /// <summary>Receipt wording that produced this alias, kept for explainability.</summary>
    public string RawText { get; private set; } = string.Empty;

    public Guid ProductId { get; private set; }

    public ProductAliasSource Source { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// A person chose the product for this wording, so the alias now points
    /// there and future receipts from the same store follow that decision.
    /// </summary>
    public void ConfirmFor(Guid productId, string rawText)
    {
        if (productId == Guid.Empty)
        {
            throw new ArgumentException("Product identifier cannot be empty.", nameof(productId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(rawText);
        ProductId = productId;
        RawText = Truncate(rawText.Trim());
        Source = ProductAliasSource.HumanConfirmed;
    }

    public static ProductAlias Create(
        string? merchantKey,
        string normalizedText,
        string rawText,
        Guid productId,
        ProductAliasSource source,
        DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedText);
        ArgumentException.ThrowIfNullOrWhiteSpace(rawText);

        if (productId == Guid.Empty)
        {
            throw new ArgumentException("Product identifier cannot be empty.", nameof(productId));
        }

        if (!Enum.IsDefined(source))
        {
            throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown alias source.");
        }

        return new ProductAlias(
            Guid.NewGuid(),
            merchantKey ?? UnknownMerchantKey,
            Truncate(normalizedText),
            Truncate(rawText.Trim()),
            productId,
            source,
            createdAt);
    }

    private static string Truncate(string value)
    {
        return value.Length <= MaximumTextLength ? value : value[..MaximumTextLength];
    }
}
