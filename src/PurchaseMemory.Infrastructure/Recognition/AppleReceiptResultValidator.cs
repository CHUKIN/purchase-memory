using System.Globalization;
using PurchaseMemory.Application.Receipts;

namespace PurchaseMemory.Infrastructure.Recognition;

internal static class AppleReceiptResultValidator
{
    private const int MaximumExtractedItems = 300;
    private const decimal MoneyTolerance = 0.02m;

    private static readonly string[] LocalDateFormats =
    [
        "yyyy-MM-dd HH:mm",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd'T'HH:mm",
        "yyyy-MM-dd'T'HH:mm:ss",
        "dd/MM/yyyy HH:mm",
        "dd/MM/yyyy HH:mm:ss",
        "dd.MM.yyyy HH:mm",
        "dd.MM.yyyy HH:mm:ss",
        "dd.MM.yyyy",
        "dd/MM/yyyy"
    ];

    public static AppleValidationResult ValidateAndCreate(AppleSidecarReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        var correctedMoneyValues = 0;
        var receiptTotal = NormalizeMoney(receipt.ReceiptTotal, upperBound: null, ref correctedMoneyValues);
        var items = (receipt.Items ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .Take(MaximumExtractedItems)
            .Select(item => CreateItem(item, receiptTotal, ref correctedMoneyValues))
            .Where(item => item is not null)
            .Cast<ReceiptExtractionItem>()
            .ToList();

        if (items.Count == 0)
        {
            throw new ReceiptExtractionException(
                "Apple recognition did not produce any plausible purchased products.");
        }

        var lineTotals = items.Where(item => item.LineTotal is not null).ToList();
        decimal? lineTotalSum = lineTotals.Count == items.Count
            ? lineTotals.Sum(item => item.LineTotal!.Value)
            : null;
        var arithmeticMatches = receiptTotal is not null
            && lineTotalSum is not null
            && Math.Abs(receiptTotal.Value - lineTotalSum.Value) <= MoneyTolerance;

        if (receiptTotal is not null
            && items.Any(item => item.LineTotal > receiptTotal + MoneyTolerance))
        {
            throw new ReceiptExtractionException(
                "Apple recognition returned a product amount larger than the entire receipt total.");
        }

        if (receiptTotal is not null && lineTotalSum is not null && receiptTotal > 0)
        {
            var relativeDifference = Math.Abs(receiptTotal.Value - lineTotalSum.Value) / receiptTotal.Value;
            if (relativeDifference > 0.50m)
            {
                throw new ReceiptExtractionException(
                    "Apple recognition failed arithmetic validation by more than 50 percent.");
            }
        }

        var extraction = new ReceiptExtractionResult(
            NormalizeOptional(receipt.MerchantName, 200),
            ParsePurchasedAt(receipt.PurchasedAt),
            NormalizeCurrency(receipt.Currency),
            receiptTotal,
            items,
            "Apple on-device hybrid",
            "Vision OCR → Translation PL→EN → Foundation Models");

        return new AppleValidationResult(
            extraction,
            arithmeticMatches,
            correctedMoneyValues,
            lineTotalSum);
    }

    private static ReceiptExtractionItem? CreateItem(
        AppleSidecarReceiptItem source,
        decimal? receiptTotal,
        ref int correctedMoneyValues)
    {
        var name = NormalizeOptional(source.Name, 500);
        if (name is null)
        {
            return null;
        }

        var quantity = source.Quantity > 0 ? source.Quantity : null;
        var discount = NormalizeMoney(source.Discount, receiptTotal, ref correctedMoneyValues) ?? 0;
        var grossLineTotal = NormalizeMoney(source.GrossLineTotal, receiptTotal, ref correctedMoneyValues);
        var lineTotal = NormalizeMoney(source.LineTotal, receiptTotal, ref correctedMoneyValues);
        var unitPrice = NormalizeMoney(source.UnitPrice, receiptTotal, ref correctedMoneyValues);

        if (discount > 0)
        {
            if (lineTotal is not null
                && grossLineTotal is not null
                && Math.Abs(lineTotal.Value - grossLineTotal.Value) <= MoneyTolerance)
            {
                // OCR often prints only the final amount after a discount. If the
                // model copied it into both fields, preserve that visible final
                // amount and reconstruct the gross value for validation.
                grossLineTotal = lineTotal + discount;
            }
            else if (lineTotal is null && grossLineTotal is not null && grossLineTotal >= discount)
            {
                lineTotal = grossLineTotal - discount;
            }
            else if (grossLineTotal is null && lineTotal is not null)
            {
                grossLineTotal = lineTotal + discount;
            }
        }

        if (lineTotal is null && quantity is not null && unitPrice is not null)
        {
            var calculatedGross = decimal.Round(
                quantity.Value * unitPrice.Value,
                2,
                MidpointRounding.AwayFromZero);
            lineTotal = calculatedGross >= discount ? calculatedGross - discount : calculatedGross;
        }

        return new ReceiptExtractionItem(
            name,
            quantity,
            NormalizeUnit(source.Unit),
            unitPrice,
            lineTotal);
    }

    private static decimal? NormalizeMoney(
        decimal? value,
        decimal? upperBound,
        ref int correctedMoneyValues)
    {
        if (value is null || value < 0)
        {
            return null;
        }

        var normalized = value.Value;
        if (upperBound is > 0)
        {
            while (normalized > upperBound.Value && normalized >= 10)
            {
                normalized /= 10;
                correctedMoneyValues++;
            }
        }

        return decimal.Round(normalized, 2, MidpointRounding.AwayFromZero);
    }

    private static DateTimeOffset? ParsePurchasedAt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTime.TryParseExact(
            value.Trim(),
            LocalDateFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out var localDate))
        {
            var unspecified = DateTime.SpecifyKind(localDate, DateTimeKind.Unspecified);
            var warsaw = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");
            return new DateTimeOffset(unspecified, warsaw.GetUtcOffset(unspecified));
        }

        if (!DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out var offsetValue))
        {
            return null;
        }

        return offsetValue;
    }

    private static string? NormalizeCurrency(string? value)
    {
        var normalized = NormalizeOptional(value, 3)?.ToUpperInvariant();
        return normalized is { Length: 3 } && normalized.All(char.IsAsciiLetterUpper)
            ? normalized
            : null;
    }

    private static string? NormalizeUnit(string? value)
    {
        var normalized = NormalizeOptional(value, 10)?.ToLowerInvariant();
        return normalized switch
        {
            "piece" or "pieces" or "pc" or "pcs" or "szt" => "pcs",
            "kg" or "g" or "l" or "ml" => normalized,
            _ => null
        };
    }

    private static string? NormalizeOptional(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized[..Math.Min(normalized.Length, maximumLength)];
    }
}

internal sealed record AppleValidationResult(
    ReceiptExtractionResult Extraction,
    bool ArithmeticMatches,
    int CorrectedMoneyValues,
    decimal? LineTotalSum);
