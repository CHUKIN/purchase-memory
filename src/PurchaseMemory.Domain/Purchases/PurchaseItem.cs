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

    public const int MaximumApproverIdLength = 200;

    public const int MaximumApproverEmailLength = 320;

    /// <summary>
    /// When a person confirmed this line matches the receipt (swipe review).
    /// Null means «not checked yet». Approving does not change the purchase's
    /// <c>UpdatedAt</c>: it is a mark on the line, not an edit of its content.
    /// </summary>
    public DateTimeOffset? ApprovedAt { get; private set; }

    /// <summary>The user id (<c>sub</c>) of the person who approved the line.</summary>
    public string? ApprovedByUserId { get; private set; }

    public string? ApprovedByEmail { get; private set; }

    public bool IsApproved => ApprovedAt is not null;

    /// <summary>
    /// Marks the line as checked. Already approved stays as it was (first
    /// approval wins), so a repeated batch is a no-op. Returns whether anything changed.
    /// </summary>
    public bool Approve(string userId, string? email, DateTimeOffset approvedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        if (IsApproved)
        {
            return false;
        }

        ApprovedAt = approvedAt;
        ApprovedByUserId = Truncate(userId.Trim(), MaximumApproverIdLength);
        ApprovedByEmail = string.IsNullOrWhiteSpace(email) ? null : Truncate(email.Trim(), MaximumApproverEmailLength);
        return true;
    }

    /// <summary>
    /// Takes the «checked» mark off, unless the line was approved after
    /// <paramref name="revokedAt"/> (a stale undo must not erase a newer approval).
    /// </summary>
    public bool RevokeApproval(DateTimeOffset revokedAt)
    {
        if (ApprovedAt is not { } approvedAt || revokedAt < approvedAt)
        {
            return false;
        }

        ApprovedAt = null;
        ApprovedByUserId = null;
        ApprovedByEmail = null;
        return true;
    }

    /// <summary>
    /// A corrected purchase replaces its line rows; the replacement of an
    /// approved line keeps the approval (who and when) of the line it replaces.
    /// </summary>
    public void CarryApprovalFrom(PurchaseItem previous)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ApprovedAt = previous.ApprovedAt;
        ApprovedByUserId = previous.ApprovedByUserId;
        ApprovedByEmail = previous.ApprovedByEmail;
    }

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

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];

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
