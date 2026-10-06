using PurchaseMemory.Infrastructure.Images;
using SkiaSharp;

namespace PurchaseMemory.IntegrationTests;

public sealed class ReceiptImageCompressorTests
{
    private static readonly ReceiptImageCompressor Compressor = new(new ReceiptDisplayCopyOptions());

    [Fact]
    public void LargePhotoIsCappedAtTheLongSideAndKeepsItsAspectRatio()
    {
        var copy = Compressor.TryCreate(TestImages.Jpeg(4000, 3000));

        Assert.NotNull(copy);
        Assert.Equal((2400, 1800), (copy.Width, copy.Height));
        using var decoded = TestImages.Decode(copy.Content);
        Assert.Equal((2400, 1800), (decoded.Width, decoded.Height));
    }

    [Fact]
    public void PortraitPhotoIsCappedByItsHeight()
    {
        var copy = Compressor.TryCreate(TestImages.Jpeg(1500, 4000));

        Assert.NotNull(copy);
        Assert.Equal((900, 2400), (copy.Width, copy.Height));
    }

    [Fact]
    public void LargeJpegIsDecodedAtAReducedScale()
    {
        // 8000×6000 → target 2400: the decoder can go to 1/2 (4000×3000), which
        // is the nearest supported size still at least as large as the target.
        var copy = Compressor.TryCreate(TestImages.Jpeg(8000, 6000, quality: 70));

        Assert.NotNull(copy);
        Assert.Equal((2400, 1800), (copy.Width, copy.Height));
        Assert.True(copy.DecodedWidth < 8000 && copy.DecodedWidth >= 2400, $"decoded at {copy.DecodedWidth}x{copy.DecodedHeight}");
    }

    [Fact]
    public void SmallPhotoIsNeverUpscaled()
    {
        var copy = Compressor.TryCreate(TestImages.Jpeg(800, 600));

        Assert.NotNull(copy);
        Assert.Equal((800, 600), (copy.Width, copy.Height));
    }

    [Fact]
    public void CopyIsAJpegOfTheConfiguredQualityAndMuchSmallerThanALargeOriginal()
    {
        var original = TestImages.Jpeg(4000, 3000, quality: 100);

        var copy = Compressor.TryCreate(original);

        Assert.NotNull(copy);
        Assert.Equal([0xFF, 0xD8, 0xFF], copy.Content[..3]);
        Assert.True(copy.Content.Length < original.Length, $"{copy.Content.Length} >= {original.Length}");
    }

    [Theory]
    [InlineData(6, 200, 400)] // rotate 90° clockwise: red block moves to the top-right
    [InlineData(8, 200, 400)] // rotate 270°: red block moves to the bottom-left
    [InlineData(3, 400, 200)] // rotate 180°: red block moves to the bottom-right
    [InlineData(1, 400, 200)] // upright: red block stays top-left
    public void ExifOrientationIsAppliedToThePixels(ushort orientation, int expectedWidth, int expectedHeight)
    {
        var original = TestImages.WithExif(TestImages.Jpeg(400, 200), orientation);

        var copy = Compressor.TryCreate(original);

        Assert.NotNull(copy);
        using var decoded = TestImages.Decode(copy.Content);
        Assert.Equal((expectedWidth, expectedHeight), (decoded.Width, decoded.Height));

        var (redX, redY) = orientation switch
        {
            6 => (expectedWidth - 10, 10),
            8 => (10, expectedHeight - 10),
            3 => (expectedWidth - 10, expectedHeight - 10),
            _ => (10, 10)
        };
        Assert.True(IsRed(decoded.GetPixel(redX, redY)), $"expected red at {redX},{redY}");
        Assert.False(IsRed(decoded.GetPixel(expectedWidth / 2, expectedHeight / 2)));
    }

    [Fact]
    public void ExifAndGpsMetadataAreStrippedFromTheCopy()
    {
        var original = TestImages.WithExif(TestImages.Jpeg(640, 480), orientation: 1);
        Assert.True(TestImages.ContainsSequence(original, "Exif\0\0"u8));
        Assert.True(TestImages.ContainsSequence(original, "SecretCam"u8));

        var copy = Compressor.TryCreate(original);

        Assert.NotNull(copy);
        Assert.False(TestImages.ContainsSequence(copy.Content, "Exif"u8));
        Assert.False(TestImages.ContainsSequence(copy.Content, "SecretCam"u8));
        Assert.False(TestImages.ContainsSequence(copy.Content, [0xFF, 0xE1]), "APP1 (EXIF/XMP) segment survived");
    }

    [Fact]
    public void PngIsConvertedAndTransparencyBecomesWhite()
    {
        var copy = Compressor.TryCreate(TestImages.Png(300, 500, transparent: true));

        Assert.NotNull(copy);
        Assert.Equal((300, 500), (copy.Width, copy.Height));
        using var decoded = TestImages.Decode(copy.Content);
        var background = decoded.GetPixel(250, 450);
        Assert.True(background.Red > 240 && background.Green > 240 && background.Blue > 240, background.ToString());
        Assert.True(IsRed(decoded.GetPixel(10, 10)));
    }

    [Fact]
    public void UndecodableBytesYieldNoCopy()
    {
        Assert.Null(Compressor.TryCreate([0xFF, 0xD8, 0xFF, 0xE0, 0x01, 0x02, 0x03]));
    }

    [Fact]
    public void PixelGuardRefusesHugeImages()
    {
        var guarded = new ReceiptImageCompressor(new ReceiptDisplayCopyOptions { MaximumSourcePixels = 1000 });

        Assert.Null(guarded.TryCreate(TestImages.Jpeg(100, 100)));
    }

    private static bool IsRed(SKColor color) => color.Red > 200 && color.Green < 60 && color.Blue < 60;
}
