namespace PurchaseMemory.Domain.Products;

/// <summary>Where a catalog product first came from.</summary>
public enum ProductOrigin
{
    /// <summary>Created from a receipt line by the matcher or by a purchase correction.</summary>
    Receipt = 0,

    /// <summary>A person typed it in by hand.</summary>
    Manual = 1,

    /// <summary>A person created it from a scanned barcode (name possibly from Open Food Facts).</summary>
    Barcode = 2
}

/// <summary>Why a product carries the «проверить» flag.</summary>
public enum ProductReviewReason
{
    /// <summary>
    /// The matcher found similar products but none clearly enough, so the line
    /// got a product of its own. A person decides whether it is one of the
    /// candidates (merge) or really a different product.
    /// </summary>
    AmbiguousMatch = 1
}
