namespace PurchaseMemory.Application.Products;

/// <summary>
/// The only tunable numbers in product matching. Each one is stated once, with
/// the reason it exists, so a wrong link can be traced to a specific decision.
/// </summary>
public static class ProductMatchingThresholds
{
    /// <summary>
    /// Similarity at which an automatic link is considered safe. Measured on real
    /// Polish grocery wording, 0.86 still accepts a stray trailing VAT letter
    /// (~0.91) while rejecting «gazowana» vs «niegazowana» (~0.83) and a changed
    /// package size (~0.80).
    /// </summary>
    public const double ConfidentMatch = 0.86;

    /// <summary>
    /// Below this the candidate is treated as unrelated rather than uncertain.
    /// Without such a floor a first-ever import would queue every line for review
    /// instead of bootstrapping the catalog. 0.62 sits above the ~0.58 that
    /// unrelated products sharing a brand or a first word reach by chance
    /// («jogurt naturalny Zott» vs «jogurt owocowy Zott»).
    /// </summary>
    public const double PlausibleCandidate = 0.62;

    /// <summary>
    /// A confident top score is still refused when the runner-up is this close,
    /// because near-ties are exactly the «250 g vs 500 g» confusions a person has
    /// to settle. Only plausible runners-up count.
    /// </summary>
    public const double DominanceMargin = 0.08;

    /// <summary>
    /// How many candidates a pending match records. Enough context for review
    /// without turning the queue into a similarity dump.
    /// </summary>
    public const int MaximumRecordedCandidates = 5;
}
