using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using PurchaseMemory.Application.Products;
using PurchaseMemory.Application.Products.Categories;
using PurchaseMemory.Application.Products.Media;
using PurchaseMemory.Application.Products.PhotoNames;
using PurchaseMemory.Application.Receipts;
using PurchaseMemory.Domain.Products;
using PurchaseMemory.Domain.Purchases;
using PurchaseMemory.Domain.Receipts;

namespace PurchaseMemory.Application.Purchases.HaulPhoto;

/// <summary>
/// «Сфотографировать покупки». Nothing is kept between the two steps: the app
/// holds the photo and sends it again with the confirmed pairs, and the
/// server prepares it the same way both times, so a box from the proposal
/// lands on the same region. Access is the household's like every purchase
/// endpoint; ids always come from the stored purchase, never from the app.
/// </summary>
public sealed partial class HaulPhotoService(
    IPurchaseDraftRepository purchases,
    IProductCatalogRepository catalog,
    IProductMediaService media,
    IHaulPhotoMatcher matcher,
    IHaulPhotoImageProcessor images,
    ILogger<HaulPhotoService> logger) : IHaulPhotoService
{
    /// <summary>The prefix of the client id a saved crop gets; it makes a repeated save a no-op.</summary>
    public const string ClientIdPrefix = "haul-";

    public bool IsEnabled => matcher.IsEnabled;

    public bool IsAvailable => matcher.IsEnabled && matcher.IsConfigured;

    public async Task<HaulPhotoProposal> ProposeAsync(Guid purchaseId, ReadOnlyMemory<byte> photo, CancellationToken cancellationToken)
    {
        if (!matcher.IsEnabled)
        {
            throw new HaulPhotoDisabledException();
        }

        ProductPhotoRules.Check(photo);
        var purchase = await FindAsync(purchaseId, cancellationToken);
        if (!matcher.IsConfigured)
        {
            MatcherNotConfigured();
            throw new HaulPhotoUnavailableException("Photos of the purchased items are not configured.");
        }

        var lines = await LinesAsync(purchase, cancellationToken);
        if (lines.Count == 0)
        {
            // Only discounts: nothing to pair, no paid call.
            return new HaulPhotoProposal([], 0);
        }

        var prepared = images.Prepare(photo)
            ?? throw new InvalidProductPhotoException(InvalidProductPhotoReason.NotAnImage, "The photo could not be read as an image.");
        var stopwatch = Stopwatch.StartNew();
        var reading = await matcher.MatchAsync(prepared.Model, lines, cancellationToken);

        // The matcher already validated its answer; checking again keeps any
        // implementation from proposing a line that is not on this purchase.
        var (items, rejected) = HaulPhotoRules.Enforce(reading.Items, lines);
        stopwatch.Stop();
        var paired = items.Count(item => item.LineId is not null);
        Proposed(lines.Count, items.Count, paired, reading.RejectedCount + rejected,
            stopwatch.ElapsedMilliseconds, matcher.ModelName, reading.InputTokens, reading.OutputTokens, reading.CostUsd);
        return new HaulPhotoProposal(items, lines.Count);
    }

    public async Task<HaulPhotoSaveResult> SaveAsync(
        Guid purchaseId,
        ReadOnlyMemory<byte> photo,
        IReadOnlyList<HaulPhotoPair> pairs,
        ReceiptActor actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pairs);
        ArgumentNullException.ThrowIfNull(actor);
        if (!matcher.IsEnabled)
        {
            throw new HaulPhotoDisabledException();
        }

        if (pairs.Count is 0 or > HaulPhotoRules.MaximumItems)
        {
            throw new ArgumentException($"Send between 1 and {HaulPhotoRules.MaximumItems} confirmed items.", nameof(pairs));
        }

        ProductPhotoRules.Check(photo);
        var purchase = await FindAsync(purchaseId, cancellationToken);
        var prepared = images.Prepare(photo)
            ?? throw new InvalidProductPhotoException(InvalidProductPhotoReason.NotAnImage, "The photo could not be read as an image.");
        var photoHash = Convert.ToHexString(SHA256.HashData(prepared.Source.Content.Span));
        var productLines = purchase.Items
            .Where(item => item.Kind == ReceiptLineKind.Product)
            .ToDictionary(item => item.Id);

        // One decode for the whole request, however many pairs it has.
        using var cropper = images.OpenForCropping(prepared.Source)
            ?? throw new InvalidProductPhotoException(InvalidProductPhotoReason.NotAnImage, "The photo could not be read as an image.");
        var results = new List<HaulPhotoPairResult>(pairs.Count);
        foreach (var pair in pairs)
        {
            results.Add(await SaveOneAsync(pair, productLines, cropper, photoHash, actor, cancellationToken));
        }

        var saved = results.Count(result => result.Status == HaulPhotoSaveStatus.Saved);
        var repeated = results.Count(result => result.Status == HaulPhotoSaveStatus.AlreadySaved);
        Saved(results.Count, saved, repeated, results.Count - saved - repeated);
        return new HaulPhotoSaveResult(results);
    }

    private async Task<HaulPhotoPairResult> SaveOneAsync(
        HaulPhotoPair pair,
        Dictionary<Guid, PurchaseItem> productLines,
        IHaulPhotoCropper cropper,
        string photoHash,
        ReceiptActor actor,
        CancellationToken cancellationToken)
    {
        if (!productLines.TryGetValue(pair.LineId, out var line))
        {
            return new HaulPhotoPairResult(pair.LineId, HaulPhotoSaveStatus.LineNotFound, null, null, false);
        }

        if (HaulBox.TryFromSize(pair.X, pair.Y, pair.Width, pair.Height) is not { } box)
        {
            return new HaulPhotoPairResult(pair.LineId, HaulPhotoSaveStatus.InvalidBox, line.ProductId, null, false);
        }

        if (line.ProductId is not { } productId)
        {
            return new HaulPhotoPairResult(pair.LineId, HaulPhotoSaveStatus.NoProduct, null, null, false);
        }

        var crop = cropper.Crop(box);
        if (crop is null)
        {
            return new HaulPhotoPairResult(pair.LineId, HaulPhotoSaveStatus.Failed, productId, null, false);
        }

        try
        {
            var upload = await media.AddPhotoAsync(productId, crop, ClientId(photoHash, line.Id, box), actor, cancellationToken);
            return new HaulPhotoPairResult(
                pair.LineId,
                upload.Created ? HaulPhotoSaveStatus.Saved : HaulPhotoSaveStatus.AlreadySaved,
                productId,
                upload.Photo.Id,
                upload.Photo.IsMain);
        }
        catch (ProductMediaLimitException)
        {
            return new HaulPhotoPairResult(pair.LineId, HaulPhotoSaveStatus.PhotoLimit, productId, null, false);
        }
        catch (ProductNotFoundException)
        {
            return new HaulPhotoPairResult(pair.LineId, HaulPhotoSaveStatus.ProductGone, productId, null, false);
        }
        catch (Exception exception) when (exception is InvalidProductPhotoException or ProductMediaSaveException)
        {
            return new HaulPhotoPairResult(pair.LineId, HaulPhotoSaveStatus.Failed, productId, null, false);
        }
    }

    /// <summary>
    /// The same photo, line and box always give the same id, so the product
    /// photo path returns the stored photo instead of adding a copy.
    /// </summary>
    internal static string ClientId(string photoHash, Guid lineId, HaulBox box)
    {
        var key = string.Join('|',
            photoHash,
            lineId.ToString("N"),
            box.X.ToString("0.0000", CultureInfo.InvariantCulture),
            box.Y.ToString("0.0000", CultureInfo.InvariantCulture),
            box.Width.ToString("0.0000", CultureInfo.InvariantCulture),
            box.Height.ToString("0.0000", CultureInfo.InvariantCulture));
        var id = ClientIdPrefix + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..32].ToLowerInvariant();
        Debug.Assert(id.Length <= ProductPhoto.MaximumClientIdLength);
        return id;
    }

    private async Task<PurchaseDraft> FindAsync(Guid purchaseId, CancellationToken cancellationToken)
    {
        if (purchaseId == Guid.Empty)
        {
            throw new PurchaseDraftNotFoundException(purchaseId);
        }

        return await purchases.FindByIdAsync(purchaseId, cancellationToken)
            ?? throw new PurchaseDraftNotFoundException(purchaseId);
    }

    private async Task<IReadOnlyList<HaulPhotoLine>> LinesAsync(PurchaseDraft purchase, CancellationToken cancellationToken)
    {
        var items = purchase.Items
            .Where(item => item.Kind == ReceiptLineKind.Product)
            .OrderBy(item => item.Position)
            .ToList();
        var linked = items.Where(item => item.ProductId is not null).Select(item => item.ProductId!.Value).ToHashSet();
        var names = linked.Count == 0
            ? new Dictionary<Guid, ProductDisplayNames>()
            : await catalog.GetProductDisplayNamesAsync(linked, cancellationToken);
        return items
            .Select(item => new HaulPhotoLine(
                item.Id,
                item.Name,
                item.ProductId is { } productId && names.TryGetValue(productId, out var display) ? Product.CleanDisplayName(display.NamePl) : null,
                item.Quantity,
                item.Unit))
            .ToList();
    }

    [LoggerMessage(EventId = 3451, Level = LogLevel.Warning,
        Message = "Haul photo is unavailable: no model or credential is configured")]
    private partial void MatcherNotConfigured();

    // Counts, timing and usage only: never the photo, never line names.
    [LoggerMessage(EventId = 3452, Level = LogLevel.Information,
        Message = "Haul photo: {LineCount} lines, {ItemCount} items found, {PairedCount} paired, {RejectedCount} rejected answers in {DurationMs} ms with model {Model}; input tokens {InputTokens}; output tokens {OutputTokens}; estimated cost USD {CostUsd}")]
    private partial void Proposed(
        int lineCount,
        int itemCount,
        int pairedCount,
        int rejectedCount,
        long durationMs,
        string model,
        long? inputTokens,
        long? outputTokens,
        decimal? costUsd);

    [LoggerMessage(EventId = 3453, Level = LogLevel.Information,
        Message = "Haul photo save: {PairCount} pairs, {SavedCount} saved, {RepeatedCount} already saved, {SkippedCount} skipped")]
    private partial void Saved(int pairCount, int savedCount, int repeatedCount, int skippedCount);
}
