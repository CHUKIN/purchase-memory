using Microsoft.Extensions.Options;

namespace PurchaseMemory.Infrastructure.Images;

public sealed class LocalReceiptBlobStorage : IReceiptBlobStorage
{
    private const int BufferSize = 64 * 1024;

    private readonly string _rootDirectory;

    public LocalReceiptBlobStorage(IOptions<ReceiptImageStorageOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Value.RootDirectory);
        _rootDirectory = Path.GetFullPath(options.Value.RootDirectory);
        Directory.CreateDirectory(_rootDirectory);
    }

    public async Task WriteAsync(
        string key,
        ReadOnlyMemory<byte> content,
        string contentType,
        CancellationToken cancellationToken)
    {
        var path = Resolve(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Write aside and rename, so a reader never sees a half-written photo.
        var temporaryPath = path + ".upload";
        try
        {
            await using (var destination = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                FileOptions.Asynchronous))
            {
                await destination.WriteAsync(content, cancellationToken);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    public Task<Stream?> TryOpenReadAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = Resolve(key);
        if (!File.Exists(path))
        {
            return Task.FromResult<Stream?>(null);
        }

        Stream stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult<Stream?>(stream);
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = Resolve(key);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        var parent = Path.GetDirectoryName(path);
        if (parent is not null
            && !string.Equals(parent, _rootDirectory, StringComparison.Ordinal)
            && Directory.Exists(parent)
            && !Directory.EnumerateFileSystemEntries(parent).Any())
        {
            Directory.Delete(parent);
        }

        return Task.CompletedTask;
    }

    public Task DeleteFolderAsync(string prefix, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var directory = Resolve(prefix);
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }

        return Task.CompletedTask;
    }

    private string Resolve(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var fullPath = Path.GetFullPath(key, _rootDirectory);
        var rootPrefix = _rootDirectory.EndsWith(Path.DirectorySeparatorChar)
            ? _rootDirectory
            : _rootDirectory + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(rootPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Receipt storage key points outside the configured directory.");
        }

        return fullPath;
    }
}
