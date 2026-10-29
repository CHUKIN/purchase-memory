using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using PurchaseMemory.Application.Products.Categories;
using PurchaseMemory.Domain.Products;
using PurchaseMemory.Infrastructure.Recognition;

namespace PurchaseMemory.Infrastructure.Categorization;

/// <summary>
/// Suggests categories, brands and human product names (Polish + Russian) with
/// a cheap OpenAI model, in one request per batch. Categories and products are
/// sent under short codes («c12», index 3) instead of GUIDs: it keeps every
/// request small, and the structured-output schema enumerates exactly the codes
/// offered, so the model cannot name a category that does not exist.
/// </summary>
public sealed class OpenAiProductCategorizer : IProductCategorizer, IDisposable
{
    private readonly ProductCategorizationOptions _options;
    private readonly IOpenAiTextTransport? _testTransport;
    private readonly Func<string?> _readApiKey;
    private readonly Lazy<OpenAiTextTransport> _productionTransport;

    public OpenAiProductCategorizer(IOptions<ProductCategorizationOptions> options)
        : this(options, null, () => Environment.GetEnvironmentVariable(OpenAiStructuredVisionProvider.ApiKeyEnvironmentVariable))
    {
    }

    internal OpenAiProductCategorizer(
        IOptions<ProductCategorizationOptions> options,
        IOpenAiTextTransport? testTransport,
        Func<string?> readApiKey)
    {
        _options = options.Value;
        _testTransport = testTransport;
        _readApiKey = readApiKey;
        _productionTransport = new Lazy<OpenAiTextTransport>(
            () => new OpenAiTextTransport(
                _readApiKey() ?? throw new InvalidOperationException("OpenAI API key is not configured."),
                _options.Model,
                TimeSpan.FromSeconds(_options.RequestTimeoutSeconds)),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public bool IsConfigured =>
        _options.Enabled
        && !string.IsNullOrWhiteSpace(_options.Model)
        && (_testTransport is not null || !string.IsNullOrWhiteSpace(_readApiKey()));

    public string ModelName => _options.Model;

    public int MaximumBatchSize => Math.Clamp(_options.BatchSize, 1, 200);

    public async Task<ProductCategorizationResult> SuggestAsync(
        IReadOnlyList<ProductToCategorize> products,
        IReadOnlyList<CategoryOption> categories,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(products);
        ArgumentNullException.ThrowIfNull(categories);
        if (!IsConfigured)
        {
            throw new ProductCategorizationException("Product categorization is not configured.");
        }

        if (products.Count == 0)
        {
            return new ProductCategorizationResult([], 0, 0);
        }

        var codes = categories
            .Select((category, index) => (Code: $"c{index + 1}", category.CategoryId))
            .ToDictionary(entry => entry.Code, entry => entry.CategoryId, StringComparer.Ordinal);
        var prompt = BuildPrompt(products, categories);
        var schema = BuildSchema(codes.Keys);

        OpenAiResponsesResult response;
        try
        {
            var transport = _testTransport ?? _productionTransport.Value;
            response = await transport.CreateAsync(
                new OpenAiTextRequest(prompt, schema, _options.Model, _options.ReasoningEffort, _options.MaximumOutputTokens),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is OpenAiServiceException or HttpRequestException or TaskCanceledException)
        {
            throw new ProductCategorizationException("OpenAI categorization request failed.", exception);
        }

        if (!string.IsNullOrWhiteSpace(response.Refusal))
        {
            throw new ProductCategorizationException("OpenAI refused to categorize the products.");
        }

        if (!string.Equals(response.Status, "completed", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(response.Json))
        {
            throw new ProductCategorizationException(
                $"OpenAI did not complete the categorization (status: {response.Status ?? "unknown"}).");
        }

        return new ProductCategorizationResult(
            Parse(response.Json, products, codes),
            response.InputTokens,
            response.OutputTokens);
    }

    internal static string BuildPrompt(IReadOnlyList<ProductToCategorize> products, IReadOnlyList<CategoryOption> categories)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Classify products bought in Polish shops into a household category tree and give each a human name.");
        builder.AppendLine("For each product pick the most specific fitting category code, or null if none fits.");
        builder.AppendLine("Brand: the manufacturer or trade mark only if the name clearly contains it (e.g. Łaciate, Mlekovita, Tymbark), else null. Never invent a brand from generic words.");
        builder.AppendLine("Names are receipt abbreviations; weight, percentage and package size are not brands. A known brand may follow the name as [brand: …].");
        builder.AppendLine();
        builder.AppendLine("namePl: the product's full Polish name as a shop shelf would show it: product type, variant, brand, size.");
        builder.AppendLine("- Expand receipt abbreviations into full Polish words (KEFI NAT → Kefir naturalny, ŚMIET → Śmietana, MAKA UNIW → Mąka uniwersalna) with correct Polish letters.");
        builder.AppendLine("- Keep only what the receipt text (or the given brand) says: never add a flavour, fat percentage, size or brand that is not there. Drop shop prefixes such as A_ or C_ and shop codes.");
        builder.AppendLine("- An abbreviation you cannot expand with confidence is left out of the name, not guessed.");
        builder.AppendLine("- Size with a space and a lower-case unit: 280 g, 1 kg, 500 ml, 1 l, 10 szt. A bare number at the end is usually grams or millilitres; leave it out if you cannot tell which.");
        builder.AppendLine("- Example: ACTIV KEFI NAT 280 → Kefir naturalny Activia 280 g.");
        builder.AppendLine("nameRu: a natural, grammatical Russian translation of namePl; the brand stays in Latin letters as in namePl; same size with Russian units (г, кг, мл, л, шт.). Example: Кефир натуральный Activia, 280 г.");
        builder.AppendLine("A name given as [namePl: …] or [nameRu: …] was set by a person: return it unchanged and make the other language a translation of it.");
        builder.AppendLine("If you are not sure what the product is, give null for both names. A wrong product is much worse than null; a short sure name (Banany / Бананы) is better than a detailed guess.");
        builder.AppendLine();
        builder.AppendLine("Categories:");
        for (var index = 0; index < categories.Count; index++)
        {
            builder.Append('c').Append((index + 1).ToString(CultureInfo.InvariantCulture)).Append(' ')
                .AppendLine(OneLine(categories[index].Path));
        }

        builder.AppendLine();
        builder.AppendLine("Products:");
        for (var index = 0; index < products.Count; index++)
        {
            builder.Append(index.ToString(CultureInfo.InvariantCulture)).Append(": ").Append(OneLine(products[index].Name));
            if (Product.CleanBrand(products[index].Brand) is { } brand)
            {
                builder.Append(" [brand: ").Append(OneLine(brand)).Append(']');
            }

            if (Product.CleanDisplayName(products[index].ConfirmedNamePl) is { } polish)
            {
                builder.Append(" [namePl: ").Append(OneLine(polish)).Append(']');
            }

            if (Product.CleanDisplayName(products[index].ConfirmedNameRu) is { } russian)
            {
                builder.Append(" [nameRu: ").Append(OneLine(russian)).Append(']');
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    /// <summary>
    /// A name is one prompt line: a line break or other control character in a
    /// receipt text could otherwise start a fake «instruction» or product row.
    /// </summary>
    internal static string OneLine(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            builder.Append(char.IsControl(character) || character is '\u2028' or '\u2029' ? ' ' : character);
        }

        return builder.ToString().Trim();
    }

    internal static string BuildSchema(IEnumerable<string> codes)
    {
        var categoryEnum = new JsonArray();
        foreach (var code in codes)
        {
            categoryEnum.Add(code);
        }

        categoryEnum.Add(null);
        var schema = new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray("items"),
            ["properties"] = new JsonObject
            {
                ["items"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["additionalProperties"] = false,
                        ["required"] = new JsonArray("index", "category", "brand", "namePl", "nameRu"),
                        ["properties"] = new JsonObject
                        {
                            ["index"] = new JsonObject { ["type"] = "integer" },
                            ["category"] = new JsonObject
                            {
                                ["type"] = new JsonArray("string", "null"),
                                ["enum"] = categoryEnum
                            },
                            ["brand"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
                            ["namePl"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
                            ["nameRu"] = new JsonObject { ["type"] = new JsonArray("string", "null") }
                        }
                    }
                }
            }
        };
        return schema.ToJsonString();
    }

    /// <summary>Unknown indexes and codes are dropped rather than trusted.</summary>
    internal static IReadOnlyList<ProductCategorySuggestion> Parse(
        string json,
        IReadOnlyList<ProductToCategorize> products,
        IReadOnlyDictionary<string, Guid> codes)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new ProductCategorizationException("OpenAI returned malformed categorization JSON.", exception);
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            {
                throw new ProductCategorizationException("OpenAI categorization JSON has no items array.");
            }

            var suggestions = new List<ProductCategorySuggestion>();
            var seen = new HashSet<int>();
            foreach (var item in items.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object
                    || !item.TryGetProperty("index", out var indexElement)
                    || !indexElement.TryGetInt32(out var index)
                    || index < 0 || index >= products.Count
                    || !seen.Add(index))
                {
                    continue;
                }

                Guid? categoryId = item.TryGetProperty("category", out var category)
                    && category.ValueKind == JsonValueKind.String
                    && codes.TryGetValue(category.GetString()!, out var id)
                        ? id
                        : null;
                suggestions.Add(new ProductCategorySuggestion(
                    products[index].ProductId,
                    categoryId,
                    ReadString(item, "brand"),
                    ReadString(item, "namePl"),
                    ReadString(item, "nameRu")));
            }

            return suggestions;
        }
    }

    private static string? ReadString(JsonElement item, string property) =>
        item.TryGetProperty(property, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;

    public void Dispose()
    {
        if (_productionTransport.IsValueCreated)
        {
            _productionTransport.Value.Dispose();
        }
    }
}
