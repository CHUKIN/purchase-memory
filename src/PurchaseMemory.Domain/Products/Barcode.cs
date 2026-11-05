namespace PurchaseMemory.Domain.Products;

/// <summary>
/// Shop barcodes: EAN-13, EAN-8, UPC-A (12 digits) and UPC-E (8 digits). A
/// UPC-A is kept as the EAN-13 it equals (a leading 0), and a UPC-E is
/// expanded to UPC-A first, so the same pack scanned in any of these formats
/// finds the same product. Mirrors <c>barcode.ts</c> in the web app.
/// </summary>
public static class Barcode
{
    /// <summary>Longest normalized code (EAN-13).</summary>
    public const int MaximumLength = 13;

    /// <summary>
    /// The normalized code, or <c>null</c> when <paramref name="raw"/> is not a
    /// valid shop barcode (wrong length or check digit). Spaces and dashes a
    /// person may type are ignored.
    /// </summary>
    public static string? TryNormalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Length > 40)
        {
            return null;
        }

        var code = string.Concat(raw.Where(character => character is not (' ' or '-')));
        if (code.Length is not (8 or 12 or 13) || !code.All(char.IsAsciiDigit))
        {
            return null;
        }

        if (code.Length == 12)
        {
            return HasValidCheckDigit(code) ? "0" + code : null;
        }

        // An 8-digit code that is a valid UPC-E is kept as its EAN-13, exactly
        // as the web app does; otherwise it has to be a valid EAN-8.
        if (code.Length == 8 && ExpandUpcE(code) is { } upcA)
        {
            return "0" + upcA;
        }

        return HasValidCheckDigit(code) ? code : null;
    }

    /// <summary>
    /// Expands an 8-digit UPC-E (number system 0 or 1) to its 12-digit UPC-A,
    /// or <c>null</c> when it is not a valid UPC-E.
    /// </summary>
    public static string? ExpandUpcE(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        if (code.Length != 8 || code[0] is not ('0' or '1') || !code.All(char.IsAsciiDigit))
        {
            return null;
        }

        var d = code.Substring(1, 6);
        var body = d[5] switch
        {
            '0' or '1' or '2' => $"{d[0]}{d[1]}{d[5]}0000{d[2]}{d[3]}{d[4]}",
            '3' => $"{d[0]}{d[1]}{d[2]}00000{d[3]}{d[4]}",
            '4' => $"{d[0]}{d[1]}{d[2]}{d[3]}00000{d[4]}",
            _ => $"{d[0]}{d[1]}{d[2]}{d[3]}{d[4]}0000{d[5]}"
        };
        var upcA = $"{code[0]}{body}{code[7]}";
        return HasValidCheckDigit(upcA) ? upcA : null;
    }

    /// <summary>GTIN check digit: weights 3 and 1 from the right, the sum rounds up to a multiple of 10.</summary>
    public static bool HasValidCheckDigit(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        if (code.Length < 2 || !code.All(char.IsAsciiDigit))
        {
            return false;
        }

        var sum = 0;
        for (var index = code.Length - 2; index >= 0; index--)
        {
            var fromRight = code.Length - 2 - index;
            sum += (code[index] - '0') * (fromRight % 2 == 0 ? 3 : 1);
        }

        return (10 - sum % 10) % 10 == code[^1] - '0';
    }
}
