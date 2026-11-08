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
    public ReceiptDisplayCopy? TryCreate(byte[] original) => TryCreate(original, out _);

    /// <summary>
    /// Same as <see cref="TryCreate(byte[])"/>; <paramref name="tooLarge"/> tells
    /// "refused because of its pixel size" apart from "not an image".
    /// </summary>
    public ReceiptDisplayCopy? TryCreate(byte[] original, out bool tooLarge)
    {
        ArgumentNullException.ThrowIfNull(original);
        tooLarge = false;

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
            tooLarge = sourceWidth > 0 && sourceHeight > 0;
            return null;
        }

        var scale = Math.Min(1d, (double)options.MaximumLongSidePixels / Math.Max(sourceWidth, sourceHeight));

        // JPEG (and WebP) decoders can downsample while decoding (1/2, 1/4, 1/8).
        // Decoding straight to the nearest size still at least as large as the
        // target keeps memory near the output size instead of ~4 bytes per
        // source pixel; the final resize then only covers the remaining factor.
        var decodeSize = ChooseDecodeSize(codec, sourceWidth, sourceHeight, scale);

        var width = Math.Max(1, (int)Math.Round(sourceWidth * scale));
        var height = Math.Max(1, (int)Math.Round(sourceHeight * scale));
        var needsResize = decodeSize.Width != width || decodeSize.Height != height;
        var origin = codec.EncodedOrigin;
        var swapsAxes = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var outputWidth = swapsAxes ? height : width;
        var outputHeight = swapsAxes ? width : height;

        // Footprint of the whole call in 4-byte pixel units, refused before allocating:
        // the decoded bitmap, the resized one (if any) and the upright output, plus,
        // for a JPEG that may be progressive, the decoder's full-size coefficient
        // buffers (kept whatever the output size: ~2 bytes x up to 4 components per
        // source pixel = 2 units).
        var footprint = (long)decodeSize.Width * decodeSize.Height
            + (needsResize ? (long)width * height : 0)
            + (long)outputWidth * outputHeight;
        if (codec.EncodedFormat == SKEncodedImageFormat.Jpeg && !IsBaselineJpeg(original))
        {
            footprint += (long)sourceWidth * sourceHeight * 2;
        }

        if (footprint > options.MaximumDecodePixels)
        {
            tooLarge = true;
            return null;
        }

        using var lease = ImageDecodeGate.ForBudget(options.MaximumConcurrentDecodePixels)
            .TryEnter(footprint, TimeSpan.FromSeconds(options.DecodeWaitSeconds));
        if (lease is null)
        {
            return null;
        }

        using var decoded = new SKBitmap(new SKImageInfo(
            decodeSize.Width, decodeSize.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        var result = codec.GetPixels(decoded.Info, decoded.GetPixels());
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            return null;
        }

        using var resized = needsResize
            ? decoded.Resize(
                new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul),
                new SKSamplingOptions(SKCubicResampler.Mitchell))
            : null;
        var source = resized ?? decoded;
        // Immutable bitmaps are wrapped by SKImage.FromBitmap instead of copied.
        source.SetImmutable();

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

        output.SetImmutable();

        using var image = SKImage.FromBitmap(output);
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, options.JpegQuality);
        if (encoded is null)
        {
            return null;
        }

        return new ReceiptDisplayCopy(encoded.ToArray(), outputWidth, outputHeight, decodeSize.Width, decodeSize.Height);
    }

    /// <summary>
    /// True only when a strict walk of the segments finds a baseline or
    /// extended-sequential frame (SOF0/SOF1) and the first scan carries all of
    /// its components: the case where libjpeg decodes straight through without
    /// full-size coefficient buffers (it buffers when the frame is progressive
    /// or the first scan has fewer components than the frame). Anything else
    /// (other frame types, non-interleaved scans, stray bytes that libjpeg would
    /// skip, truncation) returns false, so the caller charges the worst case:
    /// fail closed against parser differences.
    /// </summary>
    internal static bool IsBaselineJpeg(ReadOnlySpan<byte> jpeg)
    {
        if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8)
        {
            return false;
        }

        var frameComponents = -1;
        var i = 2;
        while (i + 3 < jpeg.Length)
        {
            if (jpeg[i] != 0xFF)
            {
                return false;
            }

            var marker = jpeg[i + 1];
            if (marker == 0xFF)
            {
                i++;
                continue;
            }

            if (marker is 0x01 or (>= 0xD0 and <= 0xD7))
            {
                i += 2;
                continue;
            }

            var length = (jpeg[i + 2] << 8) | jpeg[i + 3];
            if (length < 2 || i + 2 + length > jpeg.Length)
            {
                return false;
            }

            if (marker is 0xC0 or 0xC1)
            {
                // SOF: length(2) precision(1) height(2) width(2) Nf(1).
                if (frameComponents >= 0 || length < 8)
                {
                    return false;
                }

                frameComponents = jpeg[i + 9];
            }
            else if (marker == 0xDA)
            {
                // SOS: length(2) Ns(1). Interleaved single scan only.
                return frameComponents > 0 && length >= 3 && jpeg[i + 4] == frameComponents;
            }
            else if (marker is (>= 0xC2 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC) or 0xD8 or 0xD9)
            {
                // Any other frame type, a nested start or the end before the first scan.
                return false;
            }

            i += 2 + length;
        }

        return false;
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
