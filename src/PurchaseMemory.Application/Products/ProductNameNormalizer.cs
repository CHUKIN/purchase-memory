using System.Globalization;
using System.Text;

namespace PurchaseMemory.Application.Products;

/// <summary>
/// Turns receipt wording into a stable comparison key.
///
/// The rules are intentionally few, ordered and independent of each other, so a
/// human can predict the output of any receipt line without reading the code:
/// 1. Unicode compatibility decomposition (NFKD) plus removal of combining marks
///    folds Polish diacritics (ą ć ę ń ó ś ź ż) onto their base letters and
///    collapses ligatures and full-width forms coming from OCR;
/// 2. the Polish ł, which does not decompose, is mapped explicitly;
/// 3. lowercasing with the invariant culture, after which the German ß — also
///    non-decomposable, and the lowercase form of ẞ — expands to «ss»;
/// 4. every character that is neither a letter nor a digit becomes a space, so
///    punctuation, currency marks and asterisks stop splitting identical names;
/// 5. a boundary is inserted between a digit run and a letter run, so «250g» and
///    «250 g» become the same two tokens;
/// 6. whitespace collapses to single spaces and the result is trimmed.
///
/// Digits survive on purpose: «250 g» distinguishes a real package size.
/// </summary>
public static class ProductNameNormalizer
{
    private static readonly (char Source, char Replacement)[] NonDecomposableLetters =
    [
        ('ł', 'l'),
        ('Ł', 'L')
    ];

    /// <summary>
    /// Returns the normalized form, or an empty string when the input carries no
    /// letters or digits at all.
    /// </summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var mapped = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            var replacement = character;
            foreach (var (source, target) in NonDecomposableLetters)
            {
                if (character == source)
                {
                    replacement = target;
                    break;
                }
            }

            mapped.Append(replacement);
        }

        var decomposed = mapped.ToString().Normalize(NormalizationForm.FormKD);

        var result = new StringBuilder(decomposed.Length);
        var pendingSeparator = false;
        var previousWasDigit = false;
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                var isDigit = char.IsDigit(character);
                var boundary = pendingSeparator || (result.Length > 0 && isDigit != previousWasDigit);
                if (boundary && result.Length > 0)
                {
                    result.Append(' ');
                }

                pendingSeparator = false;
                previousWasDigit = isDigit;

                // ß has no single-letter fold, so it expands here — after
                // lowercasing, which is what turns the uppercase ẞ into it.
                var lowered = char.ToLowerInvariant(character);
                if (lowered == 'ß')
                {
                    result.Append("ss");
                }
                else
                {
                    result.Append(lowered);
                }

                continue;
            }

            pendingSeparator = true;
        }

        return result.ToString();
    }

    /// <summary>
    /// Merchant names go through the same rules; an unrecognized merchant maps to
    /// the sentinel key so store-scoped lookups stay indexable.
    /// </summary>
    public static string NormalizeMerchant(string? merchantName)
    {
        return Normalize(merchantName);
    }
}
