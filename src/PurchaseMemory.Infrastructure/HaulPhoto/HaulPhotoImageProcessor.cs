using PurchaseMemory.Application.Purchases.HaulPhoto;
using PurchaseMemory.Infrastructure.Images;
using SkiaSharp;

namespace PurchaseMemory.Infrastructure.HaulPhoto;

/// <summary>
/// Prepares a haul photo with the receipts' compressor: the EXIF rotation is
/// applied once, to the source copy, and the model copy is made from that
/// already upright copy. Boxes are fractions of the upright picture, so they
/// mean the same region on both copies and on the phone, which shows the
/// photo upright too. No EXIF (GPS, camera, time) survives re-encoding.
/// </summary>
public sealed class HaulPhotoImageProcessor : IHaulPhotoImageProcessor
{
    /// <summary>Crops of single items stay sharp enough for a product photo (stored at ≤ 1280 px anyway).</summary>
    public const int SourceLongSidePixels = 2048;

    /// <summary>
    /// Claude scales down any image above ~1.15 megapixels or 1568 px on the
    /// long side; staying under both means the model sees exactly the pixels
    /// it is told about, so its pixel boxes need no guessing.
    /// </summary>
    public const long ModelMaximumPixels = 1_150_000;

    public const int ModelMaximumLongSidePixels = 1568;

    /// <summary>A little room around the model's box: boxes tend to clip the edges of a package.</summary>
    public const double CropMargin = 0.04;

    private const int JpegQuality = 86;

    private readonly ReceiptImageCompressor _source;
    private readonly long _maximumSourcePixels;

    public HaulPhotoImageProcessor(long maximumSourcePixels = 50_000_000)
    {
        _maximumSourcePixels = maximumSourcePixels;
        _source = new ReceiptImageCompressor(new ReceiptDisplayCopyOptions
        {
            MaximumLongSidePixels = SourceLongSidePixels,
            JpegQuality = JpegQuality,
            MaximumSourcePixels = maximumSourcePixels
        });
    }

    public HaulPhotoImages? Prepare(ReadOnlyMemory<byte> upload)
    {
        var source = _source.TryCreate(upload.ToArray());
        if (source is null)
        {
            return null;
        }

        var modelLongSide = ModelLongSide(source.Width, source.Height);
        var model = new ReceiptImageCompressor(new ReceiptDisplayCopyOptions
        {
            MaximumLongSidePixels = modelLongSide,
            JpegQuality = 80,
            MaximumSourcePixels = _maximumSourcePixels
        }).TryCreate(source.Content);
        if (model is null)
        {
            return null;
        }

        return new HaulPhotoImages(
            new HaulPhotoImage(source.Content, ReceiptImageCompressor.ContentType, source.Width, source.Height),
            new HaulPhotoImage(model.Content, ReceiptImageCompressor.ContentType, model.Width, model.Height));
    }

    /// <summary>The largest long side that keeps the picture within the model's pixel and side limits.</summary>
    internal static int ModelLongSide(int width, int height)
    {
        var longSide = Math.Max(width, height);
        var shortSide = Math.Max(1, Math.Min(width, height));
        var byArea = (int)Math.Floor(Math.Sqrt(ModelMaximumPixels * (double)longSide / shortSide));
        return Math.Max(1, Math.Min(Math.Min(longSide, ModelMaximumLongSidePixels), byArea));
    }

    public IHaulPhotoCropper? OpenForCropping(HaulPhotoImage source)
    {
        ArgumentNullException.ThrowIfNull(source);
        using var data = SKData.CreateCopy(source.Content.Span);
        using var codec = SKCodec.Create(data);
        if (codec is null)
        {
            return null;
        }

        var bitmap = SKBitmap.Decode(codec);
        if (bitmap is null || bitmap.Width <= 0 || bitmap.Height <= 0)
        {
            bitmap?.Dispose();
            return null;
        }

        return new Cropper(bitmap);
    }

    private sealed class Cropper(SKBitmap bitmap) : IHaulPhotoCropper
    {
        // The source is an upright, opaque JPEG already: a subset needs no redraw.
        private readonly SKImage _whole = SKImage.FromBitmap(bitmap);

        public byte[]? Crop(HaulBox box)
        {
            ArgumentNullException.ThrowIfNull(box);
            var marginX = box.Width * CropMargin;
            var marginY = box.Height * CropMargin;
            var left = (int)Math.Floor(Math.Clamp(box.X - marginX, 0, 1) * bitmap.Width);
            var top = (int)Math.Floor(Math.Clamp(box.Y - marginY, 0, 1) * bitmap.Height);
            var right = (int)Math.Ceiling(Math.Clamp(box.X + box.Width + marginX, 0, 1) * bitmap.Width);
            var bottom = (int)Math.Ceiling(Math.Clamp(box.Y + box.Height + marginY, 0, 1) * bitmap.Height);
            if (right - left < 2 || bottom - top < 2)
            {
                return null;
            }

            using var image = _whole.Subset(SKRectI.Create(left, top, right - left, bottom - top));
            if (image is null)
            {
                return null;
            }

            using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);
            return encoded?.ToArray();
        }

        public void Dispose()
        {
            _whole.Dispose();
            bitmap.Dispose();
        }
    }
}
