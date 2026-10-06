namespace PurchaseMemory.Domain.Products;

/// <summary>
/// Who decided a product's category or brand. A human decision is
/// authoritative: automatic suggestions never replace a confirmed value.
/// </summary>
public enum ProductAttributeSource
{
    /// <summary>Proposed by the AI categorizer and not yet looked at by a person.</summary>
    Suggested = 0,

    /// <summary>Chosen or confirmed by a person.</summary>
    Confirmed = 1
}
