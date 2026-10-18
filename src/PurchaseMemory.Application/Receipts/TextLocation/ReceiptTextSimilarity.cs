using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PurchaseMemory.Application.Receipts.TextLocation;

/// <summary>
/// The text tests behind matching a recognized line to a printed OCR line, a
/// port of the measured reference (<c>score_items.normalize</c>,
/// <c>ocr_compare/boxes.py</c> <c>partial_similarity</c> / <c>price_printed</c>
/// and Python's <c>difflib.SequenceMatcher.ratio</c>), so the C# matcher scores
/// what the 110/110 study scored.
/// </summary>
public static partial class ReceiptTextSimilarity
{
    /// <summary>
    /// Lowercase, no diacritics, no VAT-group prefix ("C_", "A#"), no trailing
    /// product code, punctuation folded to single spaces.
    /// </summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var folded = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                folded.Append(character);
            }
        }

        var value = VatPrefix().Replace(folded.ToString(), string.Empty);
        value = TrailingProductCode().Replace(value, string.Empty);
        value = NonWord().Replace(value, " ");
        return string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Best ratio of the normalized name against every same-length window of
    /// the normalized text: a name printed inside a longer line still scores 1.
    /// </summary>
    public static double PartialSimilarity(string? name, string? text)
    {
        var a = Normalize(name);
        var b = Normalize(text);
        if (a.Length < 3 || b.Length == 0)
        {
            return 0;
        }

        if (b.Length <= a.Length)
        {
            return Ratio(a, b);
        }

        var best = 0.0;
        for (var start = 0; start <= b.Length - a.Length; start++)
        {
            var ratio = Ratio(a, b.Substring(start, a.Length));
            if (ratio > best)
            {
                best = ratio;
                if (best >= 1.0)
                {
                    break;
                }
            }
        }

        return best;
    }

    /// <summary>Whether the amount is printed in the text as "5.98", "5,98" or "5, 98" (sign ignored).</summary>
    public static bool PricePrinted(decimal? lineTotal, string? text)
    {
        if (lineTotal is not { } amount || string.IsNullOrEmpty(text))
        {
            return false;
        }

        var dotted = Math.Abs(amount).ToString("0.00", CultureInfo.InvariantCulture);
        var comma = dotted.Replace('.', ',');
        foreach (Match match in PrintedAmount().Matches(text))
        {
            var found = match.Value.Replace(" ", string.Empty, StringComparison.Ordinal);
            if (found == dotted || found == comma)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// <c>difflib.SequenceMatcher(None, a, b).ratio()</c>: 2·M / (|a| + |b|),
    /// M being the characters in the recursively found longest matching blocks,
    /// including difflib's "popular element" heuristic for long sequences.
    /// </summary>
    public static double Ratio(string a, string b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        var total = a.Length + b.Length;
        return total == 0 ? 1.0 : 2.0 * MatchingCharacters(a, b) / total;
    }

    private static int MatchingCharacters(string a, string b)
    {
        var positions = new Dictionary<char, List<int>>();
        for (var j = 0; j < b.Length; j++)
        {
            if (!positions.TryGetValue(b[j], out var list))
            {
                positions[b[j]] = list = [];
            }

            list.Add(j);
        }

        // difflib "autojunk": in a sequence of 200+ items, an element that makes
        // up more than 1% of it is left out of the index (but may still extend a match).
        if (b.Length >= 200)
        {
            var popularAbove = b.Length / 100 + 1;
            foreach (var popular in positions.Where(pair => pair.Value.Count > popularAbove).Select(pair => pair.Key).ToList())
            {
                positions.Remove(popular);
            }
        }

        var matched = 0;
        var queue = new Stack<(int ALow, int AHigh, int BLow, int BHigh)>();
        queue.Push((0, a.Length, 0, b.Length));
        while (queue.Count > 0)
        {
            var (aLow, aHigh, bLow, bHigh) = queue.Pop();
            var (i, j, size) = LongestMatch(a, b, positions, aLow, aHigh, bLow, bHigh);
            if (size == 0)
            {
                continue;
            }

            matched += size;
            if (aLow < i && bLow < j)
            {
                queue.Push((aLow, i, bLow, j));
            }

            if (i + size < aHigh && j + size < bHigh)
            {
                queue.Push((i + size, aHigh, j + size, bHigh));
            }
        }

        return matched;
    }

    private static (int I, int J, int Size) LongestMatch(
        string a,
        string b,
        Dictionary<char, List<int>> positions,
        int aLow,
        int aHigh,
        int bLow,
        int bHigh)
    {
        var bestI = aLow;
        var bestJ = bLow;
        var bestSize = 0;
        var lengths = new Dictionary<int, int>();
        for (var i = aLow; i < aHigh; i++)
        {
            var next = new Dictionary<int, int>();
            if (positions.TryGetValue(a[i], out var list))
            {
                foreach (var j in list)
                {
                    if (j < bLow)
                    {
                        continue;
                    }

                    if (j >= bHigh)
                    {
                        break;
                    }

                    var k = (lengths.TryGetValue(j - 1, out var previous) ? previous : 0) + 1;
                    next[j] = k;
                    if (k > bestSize)
                    {
                        bestI = i - k + 1;
                        bestJ = j - k + 1;
                        bestSize = k;
                    }
                }
            }

            lengths = next;
        }

        // No junk is defined, so only the non-junk extension of difflib applies:
        // it grows a match over "popular" characters left out of the index.
        while (bestI > aLow && bestJ > bLow && a[bestI - 1] == b[bestJ - 1])
        {
            bestI--;
            bestJ--;
            bestSize++;
        }

        while (bestI + bestSize < aHigh && bestJ + bestSize < bHigh && a[bestI + bestSize] == b[bestJ + bestSize])
        {
            bestSize++;
        }

        return (bestI, bestJ, bestSize);
    }

    [GeneratedRegex(@"^[a-g][_#]", RegexOptions.CultureInvariant)]
    private static partial Regex VatPrefix();

    [GeneratedRegex(@"\s\d{5,}[a-g]?$", RegexOptions.CultureInvariant)]
    private static partial Regex TrailingProductCode();

    [GeneratedRegex(@"[^\w%]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonWord();

    [GeneratedRegex(@"\d+[.,] ?\d{2}", RegexOptions.CultureInvariant)]
    private static partial Regex PrintedAmount();
}
