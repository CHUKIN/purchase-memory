using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PurchaseMemory.Infrastructure.Images;

namespace PurchaseMemory.IntegrationTests;

public sealed class LocalReceiptImageStoreTests
{
    private static readonly byte[] JpegBytes = [0xff, 0xd8, 0xff, 0xe0, 0x01, 0x02, 0x03];

    [Fact]
    public async Task RealPngSignatureIsRecognised()
    {
        var rootDirectory = CreateTemporaryDirectory();
        try
        {
            var store = CreateStore(rootDirectory, maximumFileSizeBytes: 1024);
            // A genuine 1x1 PNG: 89 50 4E 47 0D 0A 1A 0A, then the IHDR chunk.
            var png = Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=");
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png[..8]);
            await using var input = new MemoryStream(png);

            var stored = await store.StoreAsync(Guid.NewGuid(), 0, input, "image/jpeg", TestContext.Current.CancellationToken);

            Assert.Equal("image/png", stored.ContentType);
            Assert.EndsWith(".png", stored.StorageKey, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(rootDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task StoreAsyncUsesMagicBytesAndReturnsStableHashAndPath()
    {
        var rootDirectory = CreateTemporaryDirectory();
        try
        {
            var store = CreateStore(rootDirectory, maximumFileSizeBytes: 1024);
            var receiptId = Guid.NewGuid();
            await using var input = new MemoryStream(JpegBytes);

            var stored = await store.StoreAsync(
                receiptId,
                0,
                input,
                "image/png",
                TestContext.Current.CancellationToken);

            Assert.Equal("image/jpeg", stored.ContentType);
            Assert.Equal(JpegBytes.Length, stored.SizeBytes);
            Assert.Equal(
                Convert.ToHexString(SHA256.HashData(JpegBytes)).ToLowerInvariant(),
                stored.Sha256);
            Assert.Equal(
                Path.Combine(receiptId.ToString("N"), "part-000.jpg"),
                stored.StorageKey);

            await using var saved = await store.OpenReadAsync(
                stored.StorageKey,
                TestContext.Current.CancellationToken);
            using var copy = new MemoryStream();
            await saved.CopyToAsync(copy, TestContext.Current.CancellationToken);
            Assert.Equal(JpegBytes, copy.ToArray());

            await store.DeleteAsync(stored.StorageKey, TestContext.Current.CancellationToken);
            Assert.False(File.Exists(Path.Combine(rootDirectory, stored.StorageKey)));
        }
        finally
        {
            DeleteTemporaryDirectory(rootDirectory);
        }
    }

    [Fact]
    public async Task StoreAsyncRejectsUnsupportedAndOversizedFilesWithoutLeavingArtifacts()
    {
        var rootDirectory = CreateTemporaryDirectory();
        try
        {
            var unsupportedStore = CreateStore(rootDirectory, maximumFileSizeBytes: 1024);
            var unsupportedId = Guid.NewGuid();
            await using var unsupported = new MemoryStream("not-an-image"u8.ToArray());

            await Assert.ThrowsAsync<InvalidDataException>(() => unsupportedStore.StoreAsync(
                unsupportedId,
                0,
                unsupported,
                "image/jpeg",
                TestContext.Current.CancellationToken));

            var smallStore = CreateStore(rootDirectory, maximumFileSizeBytes: 4);
            var oversizedId = Guid.NewGuid();
            await using var oversized = new MemoryStream(JpegBytes);

            await Assert.ThrowsAsync<InvalidDataException>(() => smallStore.StoreAsync(
                oversizedId,
                0,
                oversized,
                "image/jpeg",
                TestContext.Current.CancellationToken));

            Assert.False(Directory.Exists(Path.Combine(rootDirectory, unsupportedId.ToString("N"))));
            Assert.False(Directory.Exists(Path.Combine(rootDirectory, oversizedId.ToString("N"))));
        }
        finally
        {
            DeleteTemporaryDirectory(rootDirectory);
        }
    }

    [Fact]
    public async Task OpenReadAsyncRejectsStorageKeyOutsideConfiguredRoot()
    {
        var rootDirectory = CreateTemporaryDirectory();
        try
        {
            var store = CreateStore(rootDirectory, maximumFileSizeBytes: 1024);

            await Assert.ThrowsAsync<InvalidOperationException>(() => store.OpenReadAsync(
                Path.Combine("..", "outside.jpg"),
                TestContext.Current.CancellationToken));
        }
        finally
        {
            DeleteTemporaryDirectory(rootDirectory);
        }
    }

    private static ReceiptImageStore CreateStore(string rootDirectory, long maximumFileSizeBytes)
    {
        var options = Options.Create(new ReceiptImageStorageOptions
        {
            RootDirectory = rootDirectory,
            MaximumFileSizeBytes = maximumFileSizeBytes
        });
        return new ReceiptImageStore(
            new LocalReceiptBlobStorage(options),
            options,
            NullLogger<ReceiptImageStore>.Instance);
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "purchase-memory-image-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTemporaryDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
