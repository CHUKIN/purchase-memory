using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PurchaseMemory.Application.Products.Media;

namespace PurchaseMemory.Infrastructure.OpenFoodFacts;

/// <summary>
/// Open Food Facts API v2 by barcode. Only the barcode leaves the server (no
/// user data, no key); a slow answer is cut after <see cref="Timeout"/>.
/// Answers are kept in <see cref="OpenFoodFactsCache"/>: a known product for a
/// day, «not found» for an hour; errors are not cached, so the next scan asks
/// again.
/// </summary>
public sealed partial class OpenFoodFactsClient(
    HttpClient httpClient,
    OpenFoodFactsCache cache,
    ILogger<OpenFoodFactsClient> logger) : IOpenFoodFactsClient
{
    public const string HttpClientName = "OpenFoodFacts";

    /// <summary>A shop scan waits for this at most; then «the database is not answering».</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public static readonly Uri BaseAddress = new("https://world.openfoodfacts.org/");

    /// <summary>Open Food Facts asks API clients to name themselves.</summary>
    public const string UserAgent = "PurchaseMemory/1.0 (private household app)";

    private const string Fields = "product_name,brands,image_front_small_url,quantity";

    public async Task<OpenFoodFactsLookup> LookupAsync(string code, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        if (cache.TryGet(code) is { } cached)
        {
            return cached;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        try
        {
            using var response = await httpClient.GetAsync(
                $"api/v2/product/{Uri.EscapeDataString(code)}?fields={Fields}",
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
            // 404 is how Open Food Facts says «no such product».
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return cache.Remember(code, OpenFoodFactsLookup.NotFound);
            }

            if (!response.IsSuccessStatusCode)
            {
                LookupFailed(logger, (int)response.StatusCode);
                return OpenFoodFactsLookup.Error;
            }

            await using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var document = await JsonDocument.ParseAsync(body, cancellationToken: timeout.Token);
            return cache.Remember(code, Parse(document.RootElement));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LookupTimedOut(logger, (int)Timeout.TotalSeconds);
            return OpenFoodFactsLookup.Error;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or IOException)
        {
            LookupFailed(logger, 0);
            return OpenFoodFactsLookup.Error;
        }
    }

    internal static OpenFoodFactsLookup Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("status", out var status)
            || status.ValueKind != JsonValueKind.Number
            || status.GetInt32() != 1
            || !root.TryGetProperty("product", out var product)
            || product.ValueKind != JsonValueKind.Object)
        {
            return OpenFoodFactsLookup.NotFound;
        }

        var hint = OpenFoodFactsProduct.Clean(
            Text(product, "product_name"),
            Text(product, "brands"),
            Text(product, "quantity"),
            Text(product, "image_front_small_url"));
        return hint is null ? OpenFoodFactsLookup.NotFound : new OpenFoodFactsLookup(OpenFoodFactsStatus.Found, hint);
    }

    private static string? Text(JsonElement product, string name) =>
        product.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    [LoggerMessage(EventId = 3601, Level = LogLevel.Warning, Message = "Open Food Facts lookup failed (HTTP {StatusCode})")]
    private static partial void LookupFailed(ILogger logger, int statusCode);

    [LoggerMessage(EventId = 3602, Level = LogLevel.Warning, Message = "Open Food Facts did not answer within {Seconds} s")]
    private static partial void LookupTimedOut(ILogger logger, int seconds);
}

/// <summary>
/// A small in-process cache of Open Food Facts answers, shared by all users.
/// Bounded: past <see cref="MaximumEntries"/> it is simply emptied, which at
/// household scale is rare and costs only repeat lookups.
/// </summary>
public sealed class OpenFoodFactsCache(TimeProvider timeProvider)
{
    public const int MaximumEntries = 2000;
    public static readonly TimeSpan FoundLifetime = TimeSpan.FromHours(24);
    public static readonly TimeSpan NotFoundLifetime = TimeSpan.FromHours(1);

    private readonly ConcurrentDictionary<string, (OpenFoodFactsLookup Lookup, DateTimeOffset Expires)> _entries = new(StringComparer.Ordinal);

    public OpenFoodFactsLookup? TryGet(string code)
    {
        if (!_entries.TryGetValue(code, out var entry))
        {
            return null;
        }

        if (entry.Expires > timeProvider.GetUtcNow())
        {
            return entry.Lookup;
        }

        _entries.TryRemove(code, out _);
        return null;
    }

    public OpenFoodFactsLookup Remember(string code, OpenFoodFactsLookup lookup)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        if (lookup.Status == OpenFoodFactsStatus.Error)
        {
            return lookup;
        }

        if (_entries.Count >= MaximumEntries)
        {
            _entries.Clear();
        }

        var lifetime = lookup.Status == OpenFoodFactsStatus.Found ? FoundLifetime : NotFoundLifetime;
        _entries[code] = (lookup, timeProvider.GetUtcNow() + lifetime);
        return lookup;
    }
}
