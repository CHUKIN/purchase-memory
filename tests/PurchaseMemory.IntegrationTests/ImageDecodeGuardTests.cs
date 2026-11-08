using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using PurchaseMemory.Infrastructure.Images;

namespace PurchaseMemory.IntegrationTests;

/// <summary>
/// Decompression bombs: tiny files that declare a huge picture are refused from
/// the header (before a bitmap is allocated), and the pixels being decoded at
/// once are bounded for the whole process. The haul-photo endpoint case lives in
/// <see cref="HaulPhotoTests"/>.
/// </summary>
public sealed class ImageDecodeGuardTests
{
    private static readonly ReceiptDisplayCopyOptions Defaults = new();

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    internal static byte[] Bomb(int side) => TestImages.PngDeclaring(TestImages.Png(16, 16), side, side);

    [Theory]
    [InlineData(7000)]
    [InlineData(20000)]
    public void ATinyPngDeclaringAHugePictureIsRefusedBeforeDecoding(int side)
    {
        var bomb = Bomb(side);

        Assert.True(bomb.Length < 2000);
        Assert.Null(new ReceiptImageCompressor(Defaults).TryCreate(bomb, out var tooLarge));
        Assert.True(tooLarge);
    }

    [Fact]
    public void AJpegDeclaringAnAbsurdSizeIsRefused()
    {
        var bomb = TestImages.JpegDeclaring(TestImages.Jpeg(16, 16), 20000, 20000);

        Assert.Null(new ReceiptImageCompressor(Defaults).TryCreate(bomb, out var tooLarge));
        Assert.True(tooLarge);
    }

    [Theory]
    [InlineData(7000, 7000)]
    [InlineData(8000, 6000)]
    public void AProgressiveJpegBombIsChargedForItsFullSizeCoefficientBuffers(int width, int height)
    {
        var small = ProgressiveJpegFixtures.Small();
        Assert.True(ReceiptImageCompressor.IsProgressiveJpeg(small));
        Assert.False(ReceiptImageCompressor.IsProgressiveJpeg(TestImages.Jpeg(16, 16)));
        var bomb = TestImages.JpegDeclaring(small, width, height);

        Assert.True(bomb.Length < 2000);
        Assert.Null(new ReceiptImageCompressor(Defaults).TryCreate(bomb, out var tooLarge));
        Assert.True(tooLarge);
    }

    [Fact]
    public void ANormalProgressivePhotoIsStillAccepted()
    {
        var photo = ProgressiveJpegFixtures.Phone();
        Assert.True(ReceiptImageCompressor.IsProgressiveJpeg(photo));

        var copy = new ReceiptImageCompressor(Defaults).TryCreate(photo, out var tooLarge);

        Assert.NotNull(copy);
        Assert.False(tooLarge);
        Assert.Equal(1600, copy.Width);
    }

    [Fact]
    public void NormalPhotosStillWorkAndAreNotFlaggedAsTooLarge()
    {
        var compressor = new ReceiptImageCompressor(Defaults);

        // 12 MP JPEG decodes at a reduced size; a 6 MP PNG fits the decode budget whole.
        Assert.NotNull(compressor.TryCreate(TestImages.Jpeg(4000, 3000), out var jpegTooLarge));
        Assert.False(jpegTooLarge);
        Assert.NotNull(compressor.TryCreate(TestImages.Png(3000, 2000), out var pngTooLarge));
        Assert.False(pngTooLarge);
        Assert.Null(compressor.TryCreate([0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3], out var notAnImage));
        Assert.False(notAnImage);
    }

    [Fact]
    public void ABigPngAboveTheDecodeBudgetIsRefusedButTheSameSizeJpegIsDownsampled()
    {
        var tight = new ReceiptImageCompressor(new ReceiptDisplayCopyOptions
        {
            MaximumDecodePixels = 1_000_000,
            MaximumLongSidePixels = 400
        });

        Assert.Null(tight.TryCreate(TestImages.Png(1500, 1500), out var pngTooLarge));
        Assert.True(pngTooLarge);
        Assert.NotNull(tight.TryCreate(TestImages.Jpeg(3200, 3200), out var jpegTooLarge));
        Assert.False(jpegTooLarge);
    }

    [Fact]
    public async Task TheGateNeverLetsMoreThanTheBudgetBeDecodedAtOnce()
    {
        var gate = ImageDecodeGate.ForBudget(1_234_567);
        Assert.Same(gate, ImageDecodeGate.ForBudget(1_234_567));

        var first = gate.TryEnter(800_000, TimeSpan.Zero);
        Assert.NotNull(first);
        Assert.Null(gate.TryEnter(800_000, TimeSpan.FromMilliseconds(50)));
        Assert.Equal(800_000, gate.PixelsInUse);

        // A waiter gets in as soon as the first lease is released.
        var waiter = Task.Run(() => gate.TryEnter(800_000, TimeSpan.FromSeconds(10)), Cancel);
        await Task.Delay(100, Cancel);
        Assert.False(waiter.IsCompleted);
        first.Dispose();
        first.Dispose(); // idempotent
        var second = await waiter;
        Assert.NotNull(second);
        Assert.Equal(800_000, gate.PixelsInUse);
        second.Dispose();
        Assert.Equal(0, gate.PixelsInUse);

        // Larger than the whole budget: runs alone instead of waiting forever.
        using var huge = gate.TryEnter(50_000_000, TimeSpan.Zero);
        Assert.NotNull(huge);
        Assert.Equal(1_234_567, gate.PixelsInUse);
    }

    [Fact]
    public async Task ParallelCompressionsAllFinishAndReleaseTheGate()
    {
        const long budget = 3_333_333;
        var compressor = new ReceiptImageCompressor(new ReceiptDisplayCopyOptions
        {
            MaximumConcurrentDecodePixels = budget
        });
        var photo = TestImages.Png(1200, 900);
        var gate = ImageDecodeGate.ForBudget(budget);
        long peak = 0;
        using var watching = new CancellationTokenSource();
        var watcher = Task.Run(() =>
        {
            while (!watching.IsCancellationRequested)
            {
                peak = Math.Max(peak, gate.PixelsInUse);
                Thread.Yield();
            }
        }, CancellationToken.None);

        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() => compressor.TryCreate(photo), Cancel)));
        await watching.CancelAsync();
        await watcher;

        Assert.All(results, Assert.NotNull);
        Assert.InRange(peak, 1, budget);
        Assert.Equal(0, gate.PixelsInUse);
    }

    [Fact]
    public async Task ProductPhotoUploadRefusesABombWith413()
    {
        await using var host = new AuthTestHost();
        using var client = host.CreateClientFor(AuthTestHost.Token());
        var created = await client.PostAsJsonAsync("/api/products", new { name = "Kefir" }, Cancel);
        var productId = (await created.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("id").GetGuid();

        foreach (var side in new[] { 7000, 20000 })
        {
            var content = new ByteArrayContent(Bomb(side));
            content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            var response = await client.PostAsync(
                $"/api/products/{productId}/photos",
                new MultipartFormDataContent { { content, "photo", "photo.png" } },
                Cancel);

            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
            Assert.Equal("photoTooLarge", (await response.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("code").GetString());
        }
    }
}
