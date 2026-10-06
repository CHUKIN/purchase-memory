using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace PurchaseMemory.Infrastructure.Images;

/// <summary>
/// Private-bucket storage over the S3 API (Supabase Storage, or any
/// S3-compatible store). Objects are uploaded without any ACL and never get a
/// public or pre-signed URL: photos reach people only through the API, which
/// enforces the approved-user policy.
/// </summary>
public sealed class S3ReceiptBlobStorage : IReceiptBlobStorage, IDisposable
{
    private readonly IAmazonS3 _client;
    private readonly string _bucket;
    private readonly string _keyPrefix;

    public S3ReceiptBlobStorage(IOptions<S3StorageOptions> options)
        : this(options?.Value ?? throw new ArgumentNullException(nameof(options)), CreateClient(options.Value))
    {
    }

    internal S3ReceiptBlobStorage(S3StorageOptions options, IAmazonS3 client)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Bucket);
        _client = client;
        _bucket = options.Bucket;
        _keyPrefix = options.KeyPrefix.Trim('/');
    }

    public static IAmazonS3 CreateClient(S3StorageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.ServiceUrl)
            || string.IsNullOrWhiteSpace(options.AccessKeyId)
            || string.IsNullOrWhiteSpace(options.SecretAccessKey))
        {
            throw new InvalidOperationException(
                "Storage:Provider is S3, but Storage:S3:ServiceUrl, Storage:S3:AccessKeyId or "
                + "Storage:S3:SecretAccessKey is empty. See docs/ПЕРЕМЕННЫЕ_И_СЕКРЕТЫ.md.");
        }

        var config = new AmazonS3Config
        {
            ServiceURL = options.ServiceUrl,
            AuthenticationRegion = options.Region,
            ForcePathStyle = options.ForcePathStyle,
            // SDK v4 adds flexible checksums to every request by default; many
            // S3-compatible stores reject them, and S3 itself does not need them.
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED
        };

        return new AmazonS3Client(
            new BasicAWSCredentials(options.AccessKeyId, options.SecretAccessKey),
            config);
    }

    public async Task WriteAsync(
        string key,
        ReadOnlyMemory<byte> content,
        string contentType,
        CancellationToken cancellationToken)
    {
        using var body = new MemoryStream(content.ToArray(), writable: false);
        await _client.PutObjectAsync(
            new PutObjectRequest
            {
                BucketName = _bucket,
                Key = ObjectKey(key),
                InputStream = body,
                ContentType = contentType,
                AutoCloseStream = false,
                // Plain signed payload; chunked streaming signatures are not
                // supported by every S3-compatible implementation.
                UseChunkEncoding = false
            },
            cancellationToken);
    }

    public async Task<Stream?> TryOpenReadAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _client.GetObjectAsync(_bucket, ObjectKey(key), cancellationToken);

            // Photos are at most a few megabytes. A seekable in-memory copy
            // releases the HTTP connection at once and supports range requests.
            var buffer = new MemoryStream(
                response.ContentLength is > 0 and < int.MaxValue ? (int)response.ContentLength : 0);
            await response.ResponseStream.CopyToAsync(buffer, cancellationToken);
            buffer.Position = 0;
            return buffer;
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        await _client.DeleteObjectAsync(_bucket, ObjectKey(key), cancellationToken);
    }

    public async Task DeleteFolderAsync(string prefix, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        var request = new ListObjectsV2Request
        {
            BucketName = _bucket,
            Prefix = ObjectKey(prefix.TrimEnd('/')) + "/"
        };

        // One receipt holds a handful of objects; single deletes avoid the
        // multi-object delete call, whose checksum rules vary between stores.
        ListObjectsV2Response response;
        do
        {
            response = await _client.ListObjectsV2Async(request, cancellationToken);
            foreach (var entry in response.S3Objects ?? [])
            {
                await _client.DeleteObjectAsync(_bucket, entry.Key, cancellationToken);
            }

            request.ContinuationToken = response.NextContinuationToken;
        }
        while (response.IsTruncated is true);
    }

    public void Dispose() => _client.Dispose();

    private string ObjectKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var normalized = key.Replace('\\', '/').TrimStart('/');
        if (normalized.Split('/').Any(segment => segment is "" or "." or ".."))
        {
            throw new InvalidOperationException("Receipt storage key is not a plain relative path.");
        }

        return _keyPrefix.Length == 0 ? normalized : $"{_keyPrefix}/{normalized}";
    }
}
