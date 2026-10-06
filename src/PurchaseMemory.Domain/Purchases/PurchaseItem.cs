using PurchaseMemory.Domain.Receipts;

namespace PurchaseMemory.Domain.Purchases;

/// <summary>
/// Represents one line read from a receipt. The original item name is always
/// preserved verbatim; <see cref="ProductId"/> adds the catalog link produced by
/// deterministic matching and stays null while the link is still uncertain.
/// </summary>
public sealed class PurchaseItem
{
    private PurchaseItem()
    {
        // Required by Entity Framework Core.
    }

    private PurchaseItem(
        Guid id,
        int position,
        string name,
        decimal? quantity,
        string? unit,
        decimal? unitPrice,
        decimal? lineTotal,
        ReceiptLineKind kind,
        int? discountedItemPosition)
    {
        ReceiptDiscountLinking.EnsureValid(kind, position, lineTotal, discountedItemPosition);
        Id = id;
        Position = position;
        Name = NormalizeRequiredText(name, nameof(name), 500);
        Quantity = EnsurePositive(quantity, nameof(quantity));
        Unit = NormalizeOptionalText(unit, 30);
        UnitPrice = unitPrice;
        LineTotal = lineTotal;
        Kind = kind;
        DiscountedItemPosition = discountedItemPosition;
    }

    public Guid Id { get; private set; }

    public int Position { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public decimal? Quantity { get; private set; }

    public string? Unit { get; private set; }

    public decimal? UnitPrice { get; private set; }

    public decimal? LineTotal { get; private set; }

    /// <summary>
    /// A discount line is kept verbatim so the purchase still sums to the
    /// receipt total, but it is never a product: the catalog matcher skips it
    /// and it never takes part in price history.
    /// </summary>
    public ReceiptLineKind Kind { get; private set; }

    /// <summary>For a discount: the position of the product line it reduces, if any.</summary>
    public int? DiscountedItemPosition { get; private set; }

    public bool IsDiscount => Kind == ReceiptLineKind.Discount;

    /// <summary>
    /// Catalog product this line was matched to. Null means «not matched yet»:
    /// either the purchase predates matching, or the matcher deliberately left
    /// the decision to a person and raised a pending match instead.
    /// </summary>
    public Guid? ProductId { get; private set; }

    public static PurchaseItem Create(
        int position,
        string name,
        decimal? quantity,
        string? unit,
        decimal? unitPrice,
        decimal? lineTotal,
        ReceiptLineKind kind = ReceiptLineKind.Product,
        int? discountedItemPosition = null)
    {
        if (position < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(position), "Item position cannot be negative.");
        }

        return new PurchaseItem(
            Guid.NewGuid(),
            position,
            name,
            quantity,
            unit,
            unitPrice,
            lineTotal,
            kind,
            discountedItemPosition);
    }

    public void LinkToProduct(Guid productId)
    {
        if (IsDiscount)
        {
            throw new InvalidOperationException("A discount line cannot be linked to a catalog product.");
        }

        if (productId == Guid.Empty)
        {
            throw new ArgumentException("Product identifier cannot be empty.", nameof(productId));
        }

        ProductId = productId;
    }

    public void ClearProductLink()
    {
        ProductId = null;
    }

    private static decimal? EnsurePositive(decimal? value, string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Quantity must be greater than zero.");
        }

        return value;
    }

    private static string NormalizeRequiredText(string value, string parameterName, int maximumLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException($"Value cannot exceed {maximumLength} characters.", parameterName);
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
