using SkiaSharp;

namespace PurchaseMemory.Infrastructure.Images;

/// <summary>
/// Builds the display copy of a receipt photo: EXIF orientation applied,
/// long side capped (never upscaled), transparency flattened onto white and
/// re-encoded as JPEG. Skia's encoder writes no EXIF/XMP, so GPS position,
/// camera model and capture time never reach the copy.
/// </summary>
public sealed class ReceiptImageCompressor(ReceiptDisplayCopyOptions options)
{
    public const string ContentType = "image/jpeg";

    /// <summary>
    /// Returns null when the bytes cannot be decoded or exceed the pixel guard;
    /// the caller then keeps serving the original.
    /// </summary>
    public ReceiptDisplayCopy? TryCreate(byte[] original)
    {
        ArgumentNullException.ThrowIfNull(original);

        using var data = SKData.CreateCopy(original);
        using var codec = SKCodec.Create(data);
        if (codec is null)
        {
            return null;
        }

        var sourceWidth = codec.Info.Width;
        var sourceHeight = codec.Info.Height;
        if (sourceWidth <= 0 || sourceHeight <= 0
            || (long)sourceWidth * sourceHeight > options.MaximumSourcePixels)
        {
            return null;
        }

        var scale = Math.Min(1d, (double)options.MaximumLongSidePixels / Math.Max(sourceWidth, sourceHeight));

        // JPEG (and WebP) decoders can downsample while decoding (1/2, 1/4, 1/8).
        // Decoding straight to the nearest size still at least as large as the
        // target keeps memory near the output size instead of ~4 bytes per
        // source pixel; the final resize then only covers the remaining factor.
        var decodeSize = ChooseDecodeSize(codec, sourceWidth, sourceHeight, scale);

        using var decoded = new SKBitmap(new SKImageInfo(
            decodeSize.Width, decodeSize.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        var result = codec.GetPixels(decoded.Info, decoded.GetPixels());
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            return null;
        }

        var width = Math.Max(1, (int)Math.Round(sourceWidth * scale));
        var height = Math.Max(1, (int)Math.Round(sourceHeight * scale));

        using var resized = decodeSize.Width != width || decodeSize.Height != height
            ? decoded.Resize(
                new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul),
                new SKSamplingOptions(SKCubicResampler.Mitchell))
            : null;
        var source = resized ?? decoded;

        var origin = codec.EncodedOrigin;
        var swapsAxes = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var outputWidth = swapsAxes ? height : width;
        var outputHeight = swapsAxes ? width : height;

        using var output = new SKBitmap(new SKImageInfo(
            outputWidth,
            outputHeight,
            SKColorType.Rgba8888,
            SKAlphaType.Premul));
        using (var canvas = new SKCanvas(output))
        {
            // JPEG has no alpha: transparent screenshot pixels become paper white.
            canvas.Clear(SKColors.White);
            canvas.SetMatrix(OrientationMatrix(origin, width, height));
            using var sourceImage = SKImage.FromBitmap(source);
            // Quarter turns and flips land exactly on pixels: no resampling.
            canvas.DrawImage(sourceImage, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest));
        }

        using var image = SKImage.FromBitmap(output);
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, options.JpegQuality);
        if (encoded is null)
        {
            return null;
        }

        return new ReceiptDisplayCopy(encoded.ToArray(), outputWidth, outputHeight, decodeSize.Width, decodeSize.Height);
    }

    /// <summary>
    /// Smallest size the codec can decode to natively (it snaps requests to
    /// eighths for JPEG) whose long side still reaches the target, so the copy
    /// is never upscaled from a too-small decode. Falls back to full size.
    /// </summary>
    private static SKSizeI ChooseDecodeSize(SKCodec codec, int sourceWidth, int sourceHeight, double scale)
    {
        var targetLongSide = (int)Math.Round(Math.Max(sourceWidth, sourceHeight) * scale);
        for (var eighths = 1; eighths < 8 && scale < 1d; eighths++)
        {
            var candidate = codec.GetScaledDimensions(eighths / 8f);
            if (candidate.Width > 0 && candidate.Height > 0
                && Math.Max(candidate.Width, candidate.Height) >= targetLongSide
                && Math.Max(candidate.Width, candidate.Height) < Math.Max(sourceWidth, sourceHeight))
            {
                return candidate;
            }
        }

        return new SKSizeI(sourceWidth, sourceHeight);
    }

    /// <summary>Maps source pixels (w × h, before rotation) onto the upright copy.</summary>
    private static SKMatrix OrientationMatrix(SKEncodedOrigin origin, float w, float h) => origin switch
    {
        SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
        SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
        SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
        SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightTop => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),
        SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),
        _ => SKMatrix.Identity
    };
}

/// <param name="DecodedWidth">Size the source was decoded at before the final resize.</param>
public sealed record ReceiptDisplayCopy(byte[] Content, int Width, int Height, int DecodedWidth, int DecodedHeight);
