using System.Globalization;
using System.Text.Json;
using PurchaseMemory.Domain.Receipts;

namespace PurchaseMemory.Application.Receipts;

/// <summary>
/// The shared schema-level parser and domain validator for every AI provider.
/// It deliberately rejects partial or lossy results instead of silently
/// truncating values, so provider behavior cannot diverge.
/// </summary>
public static class ReceiptStructuredOutputParser
{
    private const decimal MoneyTolerance = 0.01m;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> RootProperties =
        ["merchantName", "purchasedAt", "currency", "receiptTotal", "items"];
    private static readonly HashSet<string> ItemProperties =
        ["name", "quantity", "unit", "unitPrice", "lineTotal"];

    // Bounding boxes are asked for but never demanded: a model that cannot place
    // a value on the photo must still be able to return a valid receipt, and an
    // older prompt/schema version must keep parsing.
    //
    // rawLines (schema v3) is the model's own transcript of the item section,
    // requested only so it reads every row before building items. It is not
    // receipt data: it is accepted, never validated beyond its name, and
    // dropped here so it cannot reach the database or the logs.
    private static readonly HashSet<string> OptionalRootProperties = ["receiptTotalBoundingBox", "rawLines"];
    private static readonly HashSet<string> OptionalItemProperties = ["boundingBox"];

    public static ParsedReceipt Parse(string rawJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawJson);

        try
        {
            using var document = JsonDocument.Parse(ExtractSingleObject(rawJson));
            ValidateObjectShape(document.RootElement, RootProperties, OptionalRootProperties, "$");

            var model = document.RootElement.Deserialize<ModelReceipt>(JsonOptions)
                ?? throw Invalid("$: result cannot be null.");
            var errors = Validate(model);
            if (errors.Count > 0)
            {
                throw new ReceiptStructuredOutputException(errors);
            }

            var items = model.Items!
                .Select(item => new ReceiptExtractionItem(
                    item.Name!.Trim(),
                    item.Quantity,
                    NormalizeOptional(item.Unit),
                    item.UnitPrice,
                    item.LineTotal,
                    ReadBoundingBox(item.BoundingBox)))
                .ToArray();
            var extraction = new ReceiptExtractionResult(
                NormalizeOptional(model.MerchantName),
                ParseDate(model.PurchasedAt),
                model.Currency,
                model.ReceiptTotal,
                items,
                ExtractorName: string.Empty,
                ModelName: null,
                ReadBoundingBox(model.ReceiptTotalBoundingBox));

            return new ParsedReceipt(extraction, BuildWarnings(extraction));
        }
        catch (ReceiptStructuredOutputException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new ReceiptStructuredOutputException(
                ["$: response is not valid receipt JSON."],
                exception);
        }
    }

    private static List<string> Validate(ModelReceipt model)
    {
        var errors = new List<string>();
        ValidateOptionalLength(model.MerchantName, 200, "$.merchantName", errors);

        if (model.PurchasedAt is not null && ParseDate(model.PurchasedAt) is null)
        {
            errors.Add("$.purchasedAt must be ISO 8601 with a numeric UTC offset.");
        }

        if (model.Currency is not null
            && (model.Currency.Length != 3 || !model.Currency.All(char.IsAsciiLetterUpper)))
        {
            errors.Add("$.currency must be a three-letter uppercase ISO 4217 code.");
        }

        if (model.ReceiptTotal < 0)
        {
            errors.Add("$.receiptTotal cannot be negative.");
        }

        if (model.Items is null)
        {
            errors.Add("$.items is required.");
            return errors;
        }

        if (model.MerchantName is null
            && model.ReceiptTotal is null
            && model.Items.Count == 0)
        {
            // A model can return a schema-valid but entirely empty receipt (no
            // merchant, no total, no items). That is never a usable result, so
            // it is rejected here the same way malformed JSON is: the caller
            // retries once and then falls back to the next configured
            // pipeline instead of silently saving an empty purchase.
            errors.Add(
                "$: extraction is empty (no merchant name, receipt total, or items); "
                + "re-read the receipt and return the actual values.");
        }

        if (model.Items.Count > 300)
        {
            errors.Add("$.items cannot contain more than 300 entries.");
        }

        for (var index = 0; index < model.Items.Count; index++)
        {
            var item = model.Items[index];
            var path = $"$.items[{index}]";
            if (string.IsNullOrWhiteSpace(item.Name) || item.Name.Trim().Length > 500)
            {
                errors.Add($"{path}.name must contain 1 to 500 characters.");
            }

            if (item.Quantity is not null && item.Quantity <= 0)
            {
                errors.Add($"{path}.quantity must be greater than zero.");
            }

            ValidateOptionalLength(item.Unit, 30, $"{path}.unit", errors);
            if (item.UnitPrice < 0)
            {
                errors.Add($"{path}.unitPrice cannot be negative.");
            }

            // A negative lineTotal is accepted on any row: receipts print
            // discounts as negative lines, and OCR regularly garbles the
            // discount word itself ("OPUST" -> "OPUS A ES A ANE-"). Rejecting
            // the whole receipt for that loses every other correct line; the
            // row is kept as a discount line instead (see
            // ReceiptDiscountLineFilter) and flagged in BuildWarnings when its
            // name carries no known discount marker.
        }

        return errors;
    }

    private static List<string> BuildWarnings(ReceiptExtractionResult extraction)
    {
        var warnings = new List<string>();
        for (var index = 0; index < extraction.Items.Count; index++)
        {
            var item = extraction.Items[index];
            if (item.Quantity is not null
                && item.UnitPrice is not null
                && item.LineTotal is not null
                && Math.Abs((item.Quantity.Value * item.UnitPrice.Value) - item.LineTotal.Value) > MoneyTolerance)
            {
                warnings.Add($"Item {index + 1} total does not match quantity multiplied by unit price.");
            }

            if (item.LineTotal < 0 && !ReceiptDiscountLineFilter.IsDiscountLine(item.Name))
            {
                warnings.Add(
                    $"Item {index + 1} has a negative total but no discount marker in its name; "
                    + "it was treated as a discount and needs review.");
            }
        }

        var knownLineTotals = extraction.Items
            .Where(item => item.LineTotal is not null)
            .Select(item => item.LineTotal!.Value)
            .ToArray();
        if (extraction.ReceiptTotal is not null
            && knownLineTotals.Length == extraction.Items.Count
            && Math.Abs(knownLineTotals.Sum() - extraction.ReceiptTotal.Value) > MoneyTolerance)
        {
            warnings.Add("Receipt total does not match the sum of item totals.");
        }

        return warnings;
    }

    private static void ValidateObjectShape(
        JsonElement element,
        IReadOnlySet<string> expectedProperties,
        IReadOnlySet<string> optionalProperties,
        string path)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw Invalid($"{path} must be an object.");
        }

        var actual = element.EnumerateObject().Select(property => property.Name).ToArray();
        var missing = expectedProperties.Except(actual, StringComparer.Ordinal).ToArray();
        var unknown = actual
            .Except(expectedProperties, StringComparer.Ordinal)
            .Except(optionalProperties, StringComparer.Ordinal)
            .ToArray();
        if (missing.Length > 0 || unknown.Length > 0)
        {
            var errors = missing.Select(name => $"{path}.{name} is required.")
                .Concat(unknown.Select(name => $"{path}.{name} is not allowed."))
                .ToArray();
            throw new ReceiptStructuredOutputException(errors);
        }

        if (path == "$")
        {
            var items = element.GetProperty("items");
            if (items.ValueKind != JsonValueKind.Array)
            {
                throw Invalid("$.items must be an array.");
            }

            var index = 0;
            foreach (var item in items.EnumerateArray())
            {
                ValidateObjectShape(item, ItemProperties, OptionalItemProperties, $"$.items[{index++}]");
            }
        }
    }

    private static string ExtractSingleObject(string content)
    {
        var trimmed = content.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewLine = trimmed.IndexOf('\n');
            var closingFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewLine < 0 || closingFence <= firstNewLine)
            {
                throw Invalid("$: malformed Markdown JSON fence.");
            }

            trimmed = trimmed[(firstNewLine + 1)..closingFence].Trim();
        }

        return trimmed;
    }

    private static DateTimeOffset? ParseDate(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var hasNumericOffset = value.Length >= 6
            && (value[^6] is '+' or '-')
            && value[^3] == ':';
        if (!hasNumericOffset
            || !DateTimeOffset.TryParseExact(
                value,
                [
                    "yyyy-MM-dd'T'HH:mmzzz",
                    "yyyy-MM-dd'T'HH:mm:sszzz",
                    "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz"
                ],
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var result))
        {
            return null;
        }

        return result;
    }

    private static void ValidateOptionalLength(
        string? value,
        int maximumLength,
        string path,
        List<string> errors)
    {
        if (value is not null && (string.IsNullOrWhiteSpace(value) || value.Trim().Length > maximumLength))
        {
            errors.Add($"{path} must be null or contain 1 to {maximumLength} characters.");
        }
    }

    /// <summary>
    /// Reads a best-effort bounding box. Anything unexpected — a wrong JSON type,
    /// a missing corner, coordinates outside the photo — degrades to <c>null</c>
    /// rather than failing the whole receipt, because the text values are the
    /// result the user actually needs.
    /// </summary>
    private static BoundingBox? ReadBoundingBox(JsonElement? element)
    {
        if (element is not { ValueKind: JsonValueKind.Object } box)
        {
            return null;
        }

        return BoundingBox.TryCreate(
            ReadNumber(box, "x"),
            ReadNumber(box, "y"),
            ReadNumber(box, "width"),
            ReadNumber(box, "height"));
    }

    private static double? ReadNumber(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetDouble(out var number)
                ? number
                : null;
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ReceiptStructuredOutputException Invalid(string error) => new([error]);

    private sealed class ModelReceipt
    {
        public string? MerchantName { get; init; }
        public string? PurchasedAt { get; init; }
        public string? Currency { get; init; }
        public decimal? ReceiptTotal { get; init; }
        public IReadOnlyList<ModelReceiptItem>? Items { get; init; }

        /// <summary>Kept as raw JSON so a malformed box cannot fail deserialization.</summary>
        public JsonElement? ReceiptTotalBoundingBox { get; init; }
    }

    private sealed class ModelReceiptItem
    {
        public string? Name { get; init; }
        public decimal? Quantity { get; init; }
        public string? Unit { get; init; }
        public decimal? UnitPrice { get; init; }
        public decimal? LineTotal { get; init; }

        /// <summary>Kept as raw JSON so a malformed box cannot fail deserialization.</summary>
        public JsonElement? BoundingBox { get; init; }
    }
}

public sealed record ParsedReceipt(
    ReceiptExtractionResult Extraction,
    IReadOnlyList<string> Warnings);

public sealed class ReceiptStructuredOutputException : Exception
{
    public ReceiptStructuredOutputException(
        IReadOnlyList<string> validationErrors,
        Exception? innerException = null)
        : base(
            "The provider returned receipt JSON that does not match the receipt.extract contract.",
            innerException)
    {
        ValidationErrors = validationErrors;
    }

    public IReadOnlyList<string> ValidationErrors { get; }
}
