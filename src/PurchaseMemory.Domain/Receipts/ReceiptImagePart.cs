namespace PurchaseMemory.Domain.Receipts;

/// <summary>One persisted image in the user-defined order of a receipt capture.</summary>
public sealed class ReceiptImagePart
{
    private ReceiptImagePart()
    {
        // Required by Entity Framework Core.
    }

    private ReceiptImagePart(
        Guid id,
        int position,
        string storageKey,
        string contentType,
        long sizeBytes,
        string sha256)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Receipt image part identifier cannot be empty.", nameof(id));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(position);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sizeBytes);

        Id = id;
        Position = position;
        StorageKey = NormalizeRequiredText(storageKey, nameof(storageKey), 500);
        ContentType = NormalizeRequiredText(contentType, nameof(contentType), 100);
        SizeBytes = sizeBytes;
        Sha256 = NormalizeSha256(sha256);
    }

    public Guid Id { get; private set; }

    public int Position { get; private set; }

    public string StorageKey { get; private set; } = string.Empty;

    public string ContentType { get; private set; } = string.Empty;

    public long SizeBytes { get; private set; }

    public string Sha256 { get; private set; } = string.Empty;

    public static ReceiptImagePart Create(
        int position,
        string storageKey,
        string contentType,
        long sizeBytes,
        string sha256) =>
        new(Guid.NewGuid(), position, storageKey, contentType, sizeBytes, sha256);

    private static string NormalizeSha256(string value)
    {
        var normalized = NormalizeRequiredText(value, nameof(value), 64).ToLowerInvariant();
        if (normalized.Length != 64 || normalized.Any(character => !char.IsAsciiHexDigit(character)))
        {
            throw new ArgumentException("Image SHA-256 must contain exactly 64 hexadecimal characters.", nameof(value));
        }

        return normalized;
    }

    private static string NormalizeRequiredText(string value, string parameterName, int maximumLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException($"Value cannot exceed {maximumLength} characters.", parameterName);
        }

        return normalized;
    }
}
