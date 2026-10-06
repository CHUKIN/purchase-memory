namespace PurchaseMemory.Domain.Purchases;

/// <summary>
/// Aggregate root for a receipt recognized by an AI client but not yet reviewed
/// by a person. The first iteration stores drafts only.
/// </summary>
public sealed class PurchaseDraft
{
    private readonly List<PurchaseItem> _items = [];

    private PurchaseDraft()
    {
        // Required by Entity Framework Core.
    }

    private PurchaseDraft(Guid id, Guid? sourceReceiptId, DateTimeOffset createdAt)
    {
        Id = id;
        SourceReceiptId = sourceReceiptId;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    /// <summary>
    /// Identifies the uploaded receipt that produced this purchase. MCP-created
    /// purchases leave this null.
    /// </summary>
    public Guid? SourceReceiptId { get; private set; }

    public string? MerchantName { get; private set; }

    public DateTimeOffset? PurchasedAt { get; private set; }

    public string? Currency { get; private set; }

    public decimal? ReceiptTotal { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyCollection<PurchaseItem> Items => _items;

    public static PurchaseDraft Create(
        string? merchantName,
        DateTimeOffset? purchasedAt,
        string? currency,
        decimal? receiptTotal,
        IEnumerable<PurchaseItem> items,
        DateTimeOffset createdAt,
        Guid? sourceReceiptId = null)
    {
        if (sourceReceiptId == Guid.Empty)
        {
            throw new ArgumentException("Source receipt identifier cannot be empty.", nameof(sourceReceiptId));
        }

        var draft = new PurchaseDraft(Guid.NewGuid(), sourceReceiptId, createdAt);
        draft.ReplaceDetails(merchantName, purchasedAt, currency, receiptTotal, items, createdAt);
        return draft;
    }

    /// <summary>
    /// Replaces the complete recognized snapshot. Full replacement keeps the MCP
    /// contract unambiguous: an omitted item is intentionally removed.
    /// </summary>
    public void ReplaceDetails(
        string? merchantName,
        DateTimeOffset? purchasedAt,
        string? currency,
        decimal? receiptTotal,
        IEnumerable<PurchaseItem> items,
        DateTimeOffset updatedAt)
    {
        ArgumentNullException.ThrowIfNull(items);

        var replacementItems = items.OrderBy(item => item.Position).ToList();
        if (replacementItems.Count == 0)
        {
            throw new ArgumentException("A purchase draft must contain at least one visible item.", nameof(items));
        }

        MerchantName = NormalizeOptionalText(merchantName, 200);
        PurchasedAt = purchasedAt;
        Currency = NormalizeCurrency(currency);
        ReceiptTotal = EnsureNonNegative(receiptTotal, nameof(receiptTotal));
        UpdatedAt = updatedAt;

        _items.Clear();
        _items.AddRange(replacementItems);
    }

    private static decimal? EnsureNonNegative(decimal? value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Receipt total cannot be negative.");
        }

        return value;
    }

    private static string? NormalizeCurrency(string? currency)
    {
        if (string.IsNullOrWhiteSpace(currency))
        {
            return null;
        }

        var normalized = currency.Trim().ToUpperInvariant();
        if (normalized.Length != 3 || normalized.Any(character => !char.IsAsciiLetterUpper(character)))
        {
            throw new ArgumentException("Currency must be a three-letter ISO 4217 code, for example PLN or EUR.", nameof(currency));
        }

        return normalized;
    }

    private static string? NormalizeOptionalText(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException($"Value cannot exceed {maximumLength} characters.", nameof(value));
        }

        return normalized;
    }
}
