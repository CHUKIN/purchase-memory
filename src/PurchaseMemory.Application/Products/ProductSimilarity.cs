namespace PurchaseMemory.Application.Products;

/// <summary>
/// Similarity between two already normalized product names, in [0, 1].
///
/// Two Sørensen–Dice coefficients are computed and the smaller one wins: one over
/// character bigrams, one over whole tokens. Two names are therefore only as
/// similar as their weakest agreement.
///
/// Characters alone are too forgiving on Polish receipts, where «woda gazowana»
/// and «woda niegazowana» differ by three letters yet mean the opposite; tokens
/// alone are too strict, because OCR freely moves a word or drops a suffix. The
/// pair together punishes an inverted word while still tolerating a stray VAT
/// letter at the end of a line.
/// </summary>
public static class ProductSimilarity
{
    public static double Score(string normalizedLeft, string normalizedRight)
    {
        ArgumentNullException.ThrowIfNull(normalizedLeft);
        ArgumentNullException.ThrowIfNull(normalizedRight);

        if (normalizedLeft.Length == 0 || normalizedRight.Length == 0)
        {
            return 0;
        }

        if (string.Equals(normalizedLeft, normalizedRight, StringComparison.Ordinal))
        {
            return 1;
        }

        var characterScore = Dice(CountBigrams(normalizedLeft), CountBigrams(normalizedRight));
        var tokenScore = Dice(CountTokens(normalizedLeft), CountTokens(normalizedRight));
        return Math.Min(characterScore, tokenScore);
    }

    /// <summary>
    /// True when both names carry exactly the same numbers in the same order.
    /// Package size and percentage are the difference between two genuinely
    /// different products («150 g» vs «250 g», «3 2 %» vs «2 %»), and character
    /// overlap barely notices a single changed digit, so the matcher treats a
    /// mismatch here as a reason to ask a person rather than to link.
    /// </summary>
    public static bool NumericSignaturesMatch(string normalizedLeft, string normalizedRight)
    {
        ArgumentNullException.ThrowIfNull(normalizedLeft);
        ArgumentNullException.ThrowIfNull(normalizedRight);

        return NumericSignature(normalizedLeft).SequenceEqual(NumericSignature(normalizedRight));
    }

    private static List<string> NumericSignature(string normalized)
    {
        return normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.All(char.IsDigit))
            .ToList();
    }

    private static double Dice(Dictionary<string, int> left, Dictionary<string, int> right)
    {
        var leftTotal = left.Values.Sum();
        var rightTotal = right.Values.Sum();
        if (leftTotal == 0 || rightTotal == 0)
        {
            return 0;
        }

        var shared = 0;
        foreach (var (key, count) in left)
        {
            if (right.TryGetValue(key, out var otherCount))
            {
                shared += Math.Min(count, otherCount);
            }
        }

        return 2.0 * shared / (leftTotal + rightTotal);
    }

    private static Dictionary<string, int> CountBigrams(string value)
    {
        var bigrams = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index + 1 < value.Length; index++)
        {
            var bigram = value.Substring(index, 2);
            bigrams[bigram] = bigrams.GetValueOrDefault(bigram) + 1;
        }

        return bigrams;
    }

    private static Dictionary<string, int> CountTokens(string value)
    {
        var tokens = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var token in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            tokens[token] = tokens.GetValueOrDefault(token) + 1;
        }

        return tokens;
    }
}
