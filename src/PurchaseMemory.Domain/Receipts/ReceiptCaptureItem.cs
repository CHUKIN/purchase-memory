namespace PurchaseMemory.Domain.Receipts;

/// <summary>
/// One line recognized from an uploaded receipt before the user saves it as a purchase.
/// </summary>
public sealed class ReceiptCaptureItem
{
    private ReceiptCaptureItem()
    {
        // Required by Entity Framework Core.
    }

    private ReceiptCaptureItem(
        Guid id,
        int position,
        string name,
        decimal? quantity,
        string? unit,
        decimal? unitPrice,
        decimal? lineTotal,
        BoundingBox? boundingBox,
        ReceiptLineKind kind,
        int? discountedItemPosition)
    {
        Id = id;
        Position = EnsurePosition(position);
        Name = NormalizeRequiredText(name, nameof(name), 500);
        Quantity = EnsurePositive(quantity, nameof(quantity));
        Unit = NormalizeOptionalText(unit, 30);
        UnitPrice = EnsureNonNegative(unitPrice, nameof(unitPrice));
        ReceiptDiscountLinking.EnsureValid(kind, position, lineTotal, discountedItemPosition);
        LineTotal = lineTotal;
        BoundingBox = boundingBox;
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

    /// <summary>Best-effort location of this line on the receipt photo.</summary>
    public BoundingBox? BoundingBox { get; private set; }

    /// <summary>A product line or a discount line; a discount total is zero or negative.</summary>
    public ReceiptLineKind Kind { get; private set; }

    /// <summary>
    /// For a discount: the position of the product line it reduces, or null when
    /// no product is printed above it. Always null for a product line.
    /// </summary>
    public int? DiscountedItemPosition { get; private set; }

    public static ReceiptCaptureItem Create(
        int position,
        string name,
        decimal? quantity,
        string? unit,
        decimal? unitPrice,
        decimal? lineTotal,
        BoundingBox? boundingBox = null,
        ReceiptLineKind kind = ReceiptLineKind.Product,
        int? discountedItemPosition = null)
    {
        return new ReceiptCaptureItem(
            Guid.NewGuid(),
            position,
            name,
            quantity,
            unit,
            unitPrice,
            lineTotal,
            boundingBox,
            kind,
            discountedItemPosition);
    }

    private static int EnsurePosition(int position)
    {
        if (position < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(position), "Item position cannot be negative.");
        }

        return position;
    }

    private static decimal? EnsurePositive(decimal? value, string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Quantity must be greater than zero.");
        }

        return value;
    }

    private static decimal? EnsureNonNegative(decimal? value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Amount cannot be negative.");
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
