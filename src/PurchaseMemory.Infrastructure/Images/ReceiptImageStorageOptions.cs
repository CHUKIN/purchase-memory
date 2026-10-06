using PurchaseMemory.Application.Receipts;

namespace PurchaseMemory.Infrastructure.Images;

public enum ReceiptStorageProvider
{
    Local,
    S3
}

public sealed class ReceiptImageStorageOptions
{
    public const string SectionName = "ReceiptImages";

    /// <summary>Root directory of the <see cref="ReceiptStorageProvider.Local"/> provider.</summary>
    public string RootDirectory { get; set; } = string.Empty;

    public long MaximumFileSizeBytes { get; set; } = 15 * 1024 * 1024;

    public ReceiptDisplayCopyOptions DisplayCopy { get; set; } = new();

    /// <summary>
    /// Which copy recognition receives. The original stays the default: the
    /// recognition accuracy numbers were measured on originals, and a change of
    /// pipeline input is accepted only after a measurement on labelled data.
    /// </summary>
    public ReceiptImagePurpose AnalysisSource { get; set; } = ReceiptImagePurpose.Original;
}

public sealed class ReceiptDisplayCopyOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>Longest side of the display copy; smaller photos are never upscaled.</summary>
    public int MaximumLongSidePixels { get; set; } = 2400;

    public int JpegQuality { get; set; } = 82;

    /// <summary>Decoding guard against decompression bombs (about 8 700 × 5 800).</summary>
    public long MaximumSourcePixels { get; set; } = 50_000_000;
}

public sealed class S3StorageOptions
{
    public const string SectionName = "Storage:S3";

    /// <summary>
    /// S3 endpoint, e.g. <c>https://&lt;project&gt;.storage.supabase.co/storage/v1/s3</c>.
    /// </summary>
    public string ServiceUrl { get; set; } = string.Empty;

    public string Region { get; set; } = "us-east-1";

    public string Bucket { get; set; } = "receipts";

    /// <summary>Optional folder inside the bucket, e.g. <c>prod</c>.</summary>
    public string KeyPrefix { get; set; } = string.Empty;

    public string AccessKeyId { get; set; } = string.Empty;

    public string SecretAccessKey { get; set; } = string.Empty;

    /// <summary>Supabase Storage and most S3-compatible stores require path-style URLs.</summary>
    public bool ForcePathStyle { get; set; } = true;
}
