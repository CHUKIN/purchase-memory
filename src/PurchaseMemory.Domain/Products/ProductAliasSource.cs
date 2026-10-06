namespace PurchaseMemory.Domain.Products;

/// <summary>
/// Records how a store-scoped alias came into existence. An exact alias hit has
/// no value here on purpose: hitting an alias reuses the existing row and never
/// creates one, so the row keeps the source that originally justified it.
/// </summary>
public enum ProductAliasSource
{
    /// <summary>The catalog had nothing comparable, so the product was created from the receipt line.</summary>
    AutoCreated = 1,

    /// <summary>Deterministic similarity matching linked the line to an existing product.</summary>
    AutoMatched = 2,

    /// <summary>A person confirmed the link, for example from the future review queue.</summary>
    HumanConfirmed = 3
}
