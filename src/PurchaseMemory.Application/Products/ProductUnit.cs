namespace PurchaseMemory.Application.Products;

/// <summary>
/// Folds the unit a receipt printed onto a canonical token, so that prices are
/// only ever compared with prices measured the same way.
///
/// Two rules, both deliberate:
/// 1. <b>Synonyms fold, magnitudes do not.</b> «szt.», «SZT», «pcs» and «шт» are
///    one unit; «g» and «kg» stay two. Folding grams onto kilograms would mean
///    multiplying a recognized price by a thousand on the strength of an OCR'd
///    unit string, and one misread unit would then dominate every average.
/// 2. <b>No unit means one piece.</b> Polish receipts print the unit only for
///    goods sold by weight; a line without one is a single item. Treating it as
///    its own bucket would split the ordinary product's history in half for no
///    reason a reader could see.
/// </summary>
public static class ProductUnit
{
    /// <summary>Unit assumed for a line that states none — a single item.</summary>
    public const string Piece = "szt";

    private static readonly Dictionary<string, string> Synonyms = new(StringComparer.Ordinal)
    {
        ["szt"] = Piece,
        ["sztuka"] = Piece,
        ["sztuki"] = Piece,
        ["pcs"] = Piece,
        ["pc"] = Piece,
        ["pce"] = Piece,
        ["шт"] = Piece,
        ["kg"] = "kg",
        ["kilogram"] = "kg",
        ["g"] = "g",
        ["gr"] = "g",
        ["gram"] = "g",
        ["dag"] = "dag",
        ["l"] = "l",
        ["litr"] = "l",
        ["ltr"] = "l",
        ["ml"] = "ml",
        ["m"] = "m",
        ["opak"] = "opak",
        ["op"] = "opak"
    };

    /// <summary>
    /// Canonical unit token for one receipt line. Unknown wording is kept as its
    /// own normalized token rather than guessed at, which keeps an unfamiliar
    /// unit visibly separate instead of quietly averaged into the rest.
    /// </summary>
    public static string Canonicalize(string? unit)
    {
        // Reusing the product-name normalizer keeps «SZT.» and «szt» equal for
        // the same reasons it keeps receipt wording equal, diacritics included.
        var normalized = ProductNameNormalizer.Normalize(unit);
        if (normalized.Length == 0)
        {
            return Piece;
        }

        return Synonyms.TryGetValue(normalized, out var canonical) ? canonical : normalized;
    }
}
