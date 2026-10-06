using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PurchaseMemory.Application.Receipts;
using PurchaseMemory.Infrastructure.Images;

namespace PurchaseMemory.IntegrationTests;

/// <summary>
/// Original + display copy behaviour of <see cref="ReceiptImageStore"/>, run on
/// the local folder backend always and on a real S3 store when
/// <c>PURCHASE_MEMORY_TEST_S3_URL</c> is set (deploy/local/docker-compose.yml).
/// </summary>
public sealed class ReceiptImageStoreTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public static TheoryData<string> Backends => new() { "local", "s3" };

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task OriginalIsKeptByteForByteAndViewingGetsTheCompressedCopy(string backend)
    {
        await using var fixture = await Fixture.CreateAsync(backend);
        var original = TestImages.WithExif(TestImages.Jpeg(3200, 2400), orientation: 6);
        var receiptId = Guid.NewGuid();

        var stored = await fixture.Store.StoreAsync(receiptId, 0, new MemoryStream(original), "image/jpeg", Cancel);

        Assert.Equal("image/jpeg", stored.ContentType);
        Assert.Equal(original.Length, stored.SizeBytes);
        Assert.Equal(original, await ReadAllAsync(await fixture.Store.OpenReadAsync(stored.StorageKey, Cancel)));

        var originalContent = await fixture.Store.OpenReadAsync(stored.StorageKey, ReceiptImagePurpose.Original, Cancel);
        Assert.Null(originalContent.ContentType);
        Assert.Equal(original, await ReadAllAsync(originalContent.Content));

        var viewing = await fixture.Store.OpenReadAsync(stored.StorageKey, ReceiptImagePurpose.Viewing, Cancel);
        Assert.Equal("image/jpeg", viewing.ContentType);
        var display = await ReadAllAsync(viewing.Content);
        using var decoded = TestImages.Decode(display);
        Assert.Equal((1800, 2400), (decoded.Width, decoded.Height)); // rotated upright, long side 2400
        Assert.False(TestImages.ContainsSequence(display, "SecretCam"u8));

        // Analysis defaults to the original: recognition input is unchanged.
        var analysis = await fixture.Store.OpenReadAsync(stored.StorageKey, ReceiptImagePurpose.Analysis, Cancel);
        Assert.Equal(original, await ReadAllAsync(analysis.Content));
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task AnalysisCanBeSwitchedToTheDisplayCopy(string backend)
    {
        await using var fixture = await Fixture.CreateAsync(
            backend,
            options => options.AnalysisSource = ReceiptImagePurpose.Viewing);
        var stored = await fixture.Store.StoreAsync(
            Guid.NewGuid(), 0, new MemoryStream(TestImages.Png(500, 700)), "image/png", Cancel);

        var analysis = await fixture.Store.OpenReadAsync(stored.StorageKey, ReceiptImagePurpose.Analysis, Cancel);

        Assert.Equal("image/png", stored.ContentType);
        Assert.Equal("image/jpeg", analysis.ContentType);
        using var decoded = TestImages.Decode(await ReadAllAsync(analysis.Content));
        Assert.Equal((500, 700), (decoded.Width, decoded.Height));
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task ReceiptsWithoutADisplayCopyFallBackToTheOriginal(string backend)
    {
        await using var fixture = await Fixture.CreateAsync(backend, options => options.DisplayCopy.Enabled = false);
        var original = TestImages.Jpeg(300, 200);
        var stored = await fixture.Store.StoreAsync(Guid.NewGuid(), 0, new MemoryStream(original), "image/jpeg", Cancel);

        var viewing = await fixture.Store.OpenReadAsync(stored.StorageKey, ReceiptImagePurpose.Viewing, Cancel);

        Assert.Null(viewing.ContentType);
        Assert.Equal(original, await ReadAllAsync(viewing.Content));
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task DeletingAReceiptRemovesOriginalsAndCopies(string backend)
    {
        await using var fixture = await Fixture.CreateAsync(backend);
        var receiptId = Guid.NewGuid();
        var first = await fixture.Store.StoreAsync(receiptId, 0, new MemoryStream(TestImages.Jpeg(300, 200)), "image/jpeg", Cancel);
        var second = await fixture.Store.StoreAsync(receiptId, 1, new MemoryStream(TestImages.Jpeg(300, 200)), "image/jpeg", Cancel);
        Assert.NotNull(await fixture.Blobs.TryOpenReadAsync(ReceiptImageStore.DisplayKey(second.StorageKey), Cancel));

        await fixture.Store.DeleteAsync(first.StorageKey, Cancel);
        Assert.Null(await fixture.Blobs.TryOpenReadAsync(first.StorageKey, Cancel));
        Assert.Null(await fixture.Blobs.TryOpenReadAsync(ReceiptImageStore.DisplayKey(first.StorageKey), Cancel));
        Assert.NotNull(await fixture.Blobs.TryOpenReadAsync(second.StorageKey, Cancel));

        await fixture.Store.DeleteReceiptAsync(receiptId, Cancel);
        Assert.Null(await fixture.Blobs.TryOpenReadAsync(second.StorageKey, Cancel));
        Assert.Null(await fixture.Blobs.TryOpenReadAsync(ReceiptImageStore.DisplayKey(second.StorageKey), Cancel));
        await Assert.ThrowsAsync<FileNotFoundException>(() => fixture.Store.OpenReadAsync(second.StorageKey, Cancel));
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task FailedUploadLeavesNothingBehind(string backend)
    {
        await using var fixture = await Fixture.CreateAsync(backend, options => options.MaximumFileSizeBytes = 1024);
        var receiptId = Guid.NewGuid();
        var first = await fixture.Store.StoreAsync(receiptId, 0, new MemoryStream(TestImages.Jpeg(16, 16)), "image/jpeg", Cancel);

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Store.StoreAsync(
            receiptId, 1, new MemoryStream(TestImages.Jpeg(800, 800, quality: 100)), "image/jpeg", Cancel));

        Assert.Null(await fixture.Blobs.TryOpenReadAsync(first.StorageKey, Cancel));
    }

    [Fact]
    public async Task S3ObjectsAreNotReadableWithoutCredentials()
    {
        await using var fixture = await Fixture.CreateAsync("s3");
        var stored = await fixture.Store.StoreAsync(
            Guid.NewGuid(), 0, new MemoryStream(TestImages.Jpeg(300, 200)), "image/jpeg", Cancel);
        var s3 = fixture.S3!;

        using var anonymous = new HttpClient();
        foreach (var key in new[] { stored.StorageKey, ReceiptImageStore.DisplayKey(stored.StorageKey) })
        {
            var url = $"{s3.ServiceUrl.TrimEnd('/')}/{s3.Bucket}/{s3.KeyPrefix}/{key}";
            using var response = await anonymous.GetAsync(url, Cancel);
            Assert.True(
                response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized,
                $"anonymous GET {key} returned {(int)response.StatusCode}");
        }

        using var listing = await anonymous.GetAsync($"{s3.ServiceUrl.TrimEnd('/')}/{s3.Bucket}?list-type=2", Cancel);
        Assert.True(
            listing.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized,
            $"anonymous bucket listing returned {(int)listing.StatusCode}");

        // …while the configured credentials read it fine.
        Assert.NotNull(await fixture.Blobs.TryOpenReadAsync(stored.StorageKey, Cancel));
    }

    [Fact]
    public async Task S3KeysMustBePlainRelativePaths()
    {
        using var storage = new S3ReceiptBlobStorage(Options.Create(new S3StorageOptions
        {
            ServiceUrl = "http://127.0.0.1:1",
            AccessKeyId = "unused",
            SecretAccessKey = "unused"
        }));

        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.TryOpenReadAsync("../other/part.jpg", Cancel));
    }

    private static async Task<byte[]> ReadAllAsync(Stream stream)
    {
        await using (stream)
        {
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, Cancel);
            return buffer.ToArray();
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string? _directory;

        private Fixture(IReceiptBlobStorage blobs, ReceiptImageStore store, string? directory, S3StorageOptions? s3)
        {
            Blobs = blobs;
            Store = store;
            _directory = directory;
            S3 = s3;
        }

        public IReceiptBlobStorage Blobs { get; }

        public ReceiptImageStore Store { get; }

        public S3StorageOptions? S3 { get; }

        public static async Task<Fixture> CreateAsync(string backend, Action<ReceiptImageStorageOptions>? configure = null)
        {
            var directory = Path.Combine(Path.GetTempPath(), "purchase-memory-image-store-tests", Guid.NewGuid().ToString("N"));
            var options = new ReceiptImageStorageOptions { RootDirectory = directory };
            configure?.Invoke(options);

            IReceiptBlobStorage blobs;
            S3StorageOptions? s3 = null;
            if (backend == "s3")
            {
                s3 = ExternalTestServices.S3Options(keyPrefix: Guid.NewGuid().ToString("N"));
                Assert.SkipWhen(s3 is null, $"Set {ExternalTestServices.S3UrlVariable} to run against a real S3 store.");
                await ExternalTestServices.EnsureBucketAsync(s3!);
                blobs = new S3ReceiptBlobStorage(Options.Create(s3!));
                directory = null!;
            }
            else
            {
                blobs = new LocalReceiptBlobStorage(Options.Create(options));
            }

            var store = new ReceiptImageStore(blobs, Options.Create(options), NullLogger<ReceiptImageStore>.Instance);
            return new Fixture(blobs, store, directory, s3);
        }

        public async ValueTask DisposeAsync()
        {
            if (S3 is not null)
            {
                await ExternalTestServices.DeletePrefixAsync(S3);
            }

            (Blobs as IDisposable)?.Dispose();
            if (_directory is not null && Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }
}
