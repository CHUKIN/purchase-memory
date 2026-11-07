using PurchaseMemory.Application.Receipts;

namespace PurchaseMemory.Application.Purchases.HaulPhoto;

/// <summary>
/// «Сфотографировать покупки»: one photo of everything bought on a saved
/// purchase. The implementation calls a paid vision model with the photo and
/// the purchase's lines (short codes and names, no prices) and gets back a
/// box per item and the line it most likely is. The photo is sent once and
/// never logged.
/// </summary>
public interface IHaulPhotoMatcher
{
    /// <summary>The feature switch. Off: the endpoints say «disabled» and the UI hides the button.</summary>
    bool IsEnabled { get; }

    /// <summary>Enabled and has a model and a credential; otherwise nothing is called.</summary>
    bool IsConfigured { get; }

    /// <summary>The model id, for logs and the measurement tool.</summary>
    string ModelName { get; }

    /// <summary>
    /// Fails only with <see cref="HaulPhotoUnavailableException"/> (not
    /// configured, provider error, refusal, malformed answer). Lines in the
    /// answer are only ever ids from <paramref name="lines"/>.
    /// </summary>
    Task<HaulPhotoReading> MatchAsync(
        HaulPhotoImage image,
        IReadOnlyList<HaulPhotoLine> lines,
        CancellationToken cancellationToken);
}

/// <summary>
/// Turns an upload into the upright copies the feature works with and cuts
/// crops out of them. Both copies come from the same decoded, EXIF-rotated
/// picture, so a box the model gives on one is the same region on the other.
/// </summary>
public interface IHaulPhotoImageProcessor
{
    /// <summary>Null when the bytes cannot be decoded as an image (or are absurdly large in pixels).</summary>
    HaulPhotoImages? Prepare(ReadOnlyMemory<byte> upload);

    /// <summary>
    /// Decodes <paramref name="source"/> once for any number of crops; null
    /// when it cannot be decoded. The caller disposes the result.
    /// </summary>
    IHaulPhotoCropper? OpenForCropping(HaulPhotoImage source);
}

/// <summary>One decoded photo that crops are cut from.</summary>
public interface IHaulPhotoCropper : IDisposable
{
    /// <summary>A JPEG of <paramref name="box"/> (with a small margin); null on failure.</summary>
    byte[]? Crop(HaulBox box);
}

/// <summary>An upright, metadata-free JPEG and its size in pixels.</summary>
public sealed record HaulPhotoImage(ReadOnlyMemory<byte> Content, string ContentType, int Width, int Height);

/// <param name="Source">The larger copy crops are cut from.</param>
/// <param name="Model">The copy sent to the model, small enough that the model sees it at its own pixel size.</param>
public sealed record HaulPhotoImages(HaulPhotoImage Source, HaulPhotoImage Model);

/// <summary>What the model is told about one product line of the purchase.</summary>
/// <param name="ProductName">The linked product's human (Polish) name, when it has one.</param>
public sealed record HaulPhotoLine(Guid LineId, string ReceiptName, string? ProductName, decimal? Quantity, string? Unit);

public enum HaulMatchConfidence
{
    Low,
    Medium,
    High
}

/// <summary>
/// A rectangle as fractions of the upright photo (0..1, left/top origin). Only
/// <see cref="TryCreate"/> makes one, so every box in the feature is inside
/// the photo and large enough to be a useful product photo.
/// </summary>
public sealed record HaulBox
{
    /// <summary>A side shorter than this (2 % of the photo) would make an unusable crop.</summary>
    public const double MinimumSide = 0.02;

    private HaulBox(double x, double y, double width, double height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public double X { get; }

    public double Y { get; }

    public double Width { get; }

    public double Height { get; }

    /// <summary>
    /// Clamps the edges into the photo and orders them; null for NaN,
    /// infinities, or a box that is too small after clamping.
    /// </summary>
    public static HaulBox? TryCreate(double left, double top, double right, double bottom)
    {
        if (!double.IsFinite(left) || !double.IsFinite(top) || !double.IsFinite(right) || !double.IsFinite(bottom))
        {
            return null;
        }

        var x1 = Math.Clamp(Math.Min(left, right), 0d, 1d);
        var x2 = Math.Clamp(Math.Max(left, right), 0d, 1d);
        var y1 = Math.Clamp(Math.Min(top, bottom), 0d, 1d);
        var y2 = Math.Clamp(Math.Max(top, bottom), 0d, 1d);
        if (x2 - x1 < MinimumSide || y2 - y1 < MinimumSide)
        {
            return null;
        }

        return new HaulBox(Math.Round(x1, 4), Math.Round(y1, 4), Math.Round(x2 - x1, 4), Math.Round(y2 - y1, 4));
    }

    /// <summary>The same as <see cref="TryCreate"/> for a box given as left, top, width and height.</summary>
    public static HaulBox? TryFromSize(double x, double y, double width, double height) =>
        TryCreate(x, y, x + width, y + height);
}

/// <summary>One item the model found. <see cref="LineId"/> is null when no line fits (or the model's line was dropped).</summary>
public sealed record HaulPhotoDetection(HaulBox Box, Guid? LineId, HaulMatchConfidence Confidence);

/// <summary>
/// The model's items in its own order. <see cref="RejectedCount"/> counts the
/// answers that were dropped or lost their line (an unknown code, a bad box,
/// a line used more often than it was bought, items past the cap).
/// </summary>
public sealed record HaulPhotoReading(
    IReadOnlyList<HaulPhotoDetection> Items,
    int RejectedCount,
    long? InputTokens,
    long? OutputTokens,
    decimal? CostUsd);

/// <summary>The model cannot be asked right now; the cause is in the log.</summary>
public sealed class HaulPhotoUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>The feature switch is off on this server.</summary>
public sealed class HaulPhotoDisabledException()
    : Exception("Photos of the purchased items are switched off on this server.");

public static class HaulPhotoRules
{
    /// <summary>A table photo rarely shows more; it also caps the answer and one save request.</summary>
    public const int MaximumItems = 40;

    /// <summary>The most items one line can be paired with (a «12 szt» line still gives at most this many photos).</summary>
    public const int MaximumUsesPerLine = 10;

    /// <summary>
    /// How many photographed items one line may be paired with: a whole
    /// quantity above one («3 szt») allows that many, anything else (one
    /// piece, a weight like 0.45 kg, unknown) allows one.
    /// </summary>
    public static int AllowedUses(decimal? quantity) =>
        quantity is { } value && value > 1m && value == decimal.Truncate(value)
            ? (int)Math.Min(value, MaximumUsesPerLine)
            : 1;

    /// <summary>
    /// The strict check of a model answer, shared by the matcher and the
    /// service: at most <see cref="MaximumItems"/> items; a line that is not
    /// one of <paramref name="lines"/> is cleared; a line used more often than
    /// <see cref="AllowedUses"/> keeps only its most confident items (the rest
    /// stay as items without a line, so the person can still pair them).
    /// Returns the items in their original order and how many answers lost
    /// their line or were dropped.
    /// </summary>
    public static (IReadOnlyList<HaulPhotoDetection> Items, int Rejected) Enforce(
        IReadOnlyList<HaulPhotoDetection> items,
        IReadOnlyList<HaulPhotoLine> lines)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(lines);
        var rejected = Math.Max(0, items.Count - MaximumItems);
        var kept = items.Take(MaximumItems).ToArray();
        var allowed = lines.GroupBy(line => line.LineId).ToDictionary(group => group.Key, group => AllowedUses(group.First().Quantity));
        var used = new Dictionary<Guid, int>();

        // Most confident first; for equal confidence the model's order decides.
        foreach (var index in Enumerable.Range(0, kept.Length).OrderByDescending(index => kept[index].Confidence).ThenBy(index => index))
        {
            if (kept[index].LineId is not { } lineId)
            {
                continue;
            }

            var count = used.GetValueOrDefault(lineId);
            if (!allowed.TryGetValue(lineId, out var limit) || count >= limit)
            {
                kept[index] = kept[index] with { LineId = null };
                rejected++;
                continue;
            }

            used[lineId] = count + 1;
        }

        return (kept, rejected);
    }
}

public interface IHaulPhotoService
{
    /// <summary>The feature switch alone.</summary>
    bool IsEnabled { get; }

    /// <summary>Whether the button should show: switched on and configured.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Finds the items on the photo and proposes a line for each. Nothing is stored.
    /// </summary>
    /// <exception cref="HaulPhotoDisabledException">The switch is off.</exception>
    /// <exception cref="Products.PhotoNames.InvalidProductPhotoException">Empty, too large or not an image.</exception>
    /// <exception cref="PurchaseDraftNotFoundException">No such purchase.</exception>
    /// <exception cref="HaulPhotoUnavailableException">No model/credential, or the model failed.</exception>
    Task<HaulPhotoProposal> ProposeAsync(Guid purchaseId, ReadOnlyMemory<byte> photo, CancellationToken cancellationToken);

    /// <summary>
    /// Cuts each confirmed item out of the same photo (sent again by the app)
    /// and adds it as a photo of the line's product, through the ordinary
    /// product photo path (its limits; the first photo becomes the main one, an
    /// existing main photo stays). Every pair gets its own outcome. Repeating
    /// the same request adds nothing twice.
    /// </summary>
    /// <exception cref="HaulPhotoDisabledException">The switch is off.</exception>
    /// <exception cref="ArgumentException">No pairs, or more than <see cref="HaulPhotoRules.MaximumItems"/>.</exception>
    /// <exception cref="Products.PhotoNames.InvalidProductPhotoException">Empty, too large or not an image.</exception>
    /// <exception cref="PurchaseDraftNotFoundException">No such purchase.</exception>
    Task<HaulPhotoSaveResult> SaveAsync(
        Guid purchaseId,
        ReadOnlyMemory<byte> photo,
        IReadOnlyList<HaulPhotoPair> pairs,
        ReceiptActor actor,
        CancellationToken cancellationToken);
}

/// <param name="LineCount">How many product lines the model chose from.</param>
public sealed record HaulPhotoProposal(IReadOnlyList<HaulPhotoDetection> Items, int LineCount);

/// <summary>A confirmed pair as the app sends it back: the line and the item's box from the proposal.</summary>
public sealed record HaulPhotoPair(Guid LineId, double X, double Y, double Width, double Height);

public enum HaulPhotoSaveStatus
{
    /// <summary>A new photo of the line's product.</summary>
    Saved,

    /// <summary>This exact crop was already saved (a repeated request).</summary>
    AlreadySaved,

    /// <summary>The line has no catalog product to attach a photo to.</summary>
    NoProduct,

    /// <summary>Not a product line of this purchase (removed or edited since).</summary>
    LineNotFound,

    /// <summary>The box is outside the photo or too small.</summary>
    InvalidBox,

    /// <summary>The product already has the most photos it may have.</summary>
    PhotoLimit,

    /// <summary>The product was deleted or merged away meanwhile.</summary>
    ProductGone,

    /// <summary>The crop could not be made or stored.</summary>
    Failed
}

public sealed record HaulPhotoPairResult(
    Guid LineId,
    HaulPhotoSaveStatus Status,
    Guid? ProductId,
    Guid? PhotoId,
    bool IsMain);

public sealed record HaulPhotoSaveResult(IReadOnlyList<HaulPhotoPairResult> Results);
