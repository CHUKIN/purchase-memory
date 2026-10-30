namespace PurchaseMemory.Application.Products.PhotoNames;

/// <summary>
/// Reads a product's name from a photo of its package (brand, product,
/// variant, size) and writes it the way stage 1.4 names look: Polish as on
/// the shelf, Russian as a translation. Implementations call a paid vision
/// model; the photo is sent once and never stored or logged.
/// </summary>
public interface IProductPhotoNamer
{
    /// <summary>False without a model or a key; the endpoint then answers 503 without calling anything.</summary>
    bool IsConfigured { get; }

    /// <summary>The model id, for the audit actor and the measurement tool.</summary>
    string ModelName { get; }

    /// <summary>
    /// Fails only with <see cref="ProductPhotoNamingUnavailableException"/>
    /// (not configured, provider error, refusal, malformed answer).
    /// </summary>
    Task<ProductPhotoReading> ReadAsync(
        ProductPhotoImage image,
        ProductPhotoHint? hint,
        CancellationToken cancellationToken);
}

/// <summary>An already checked image: JPEG, PNG or WebP bytes.</summary>
public sealed record ProductPhotoImage(ReadOnlyMemory<byte> Content, string ContentType);

/// <summary>What the catalog already knows; context only, the package text wins.</summary>
public sealed record ProductPhotoHint(string ReceiptText, string? Brand);

/// <summary>
/// The model's reading. A name is <c>null</c> when the model was not sure;
/// usage numbers are for cost logging and the measurement tool.
/// </summary>
public sealed record ProductPhotoReading(
    string? Brand,
    string? NamePl,
    string? NameRu,
    long? InputTokens,
    long? OutputTokens,
    decimal? CostUsd);

/// <summary>Names cannot be read from a photo right now; the cause is in the log.</summary>
public sealed class ProductPhotoNamingUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>The uploaded file is not something the namer may be given.</summary>
public sealed class InvalidProductPhotoException(InvalidProductPhotoReason reason, string message)
    : Exception(message)
{
    public InvalidProductPhotoReason Reason { get; } = reason;
}

public enum InvalidProductPhotoReason
{
    Empty,
    TooLarge,
    NotAnImage
}

/// <summary>
/// The photo the server accepts: one JPEG, PNG or WebP of at most 1.5 MB. The
/// client sends a ~200 KB JPEG; the format is taken from the bytes, never
/// from the declared content type.
/// </summary>
public static class ProductPhotoRules
{
    public const int MaximumBytes = 1536 * 1024;

    public static ProductPhotoImage Check(ReadOnlyMemory<byte> content)
    {
        if (content.IsEmpty)
        {
            throw new InvalidProductPhotoException(InvalidProductPhotoReason.Empty, "The photo is empty.");
        }

        if (content.Length > MaximumBytes)
        {
            throw new InvalidProductPhotoException(
                InvalidProductPhotoReason.TooLarge,
                $"The photo is larger than {MaximumBytes / 1024} KB. Send a compressed JPEG.");
        }

        var contentType = DetectContentType(content.Span)
            ?? throw new InvalidProductPhotoException(
                InvalidProductPhotoReason.NotAnImage,
                "Only JPEG, PNG and WebP photos are accepted.");
        return new ProductPhotoImage(content, contentType);
    }

    // A byte array, not a "\x89PNG…"u8 literal: in C# that escape is U+0089.
    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    internal static string? DetectContentType(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 8 && header[..8].SequenceEqual(PngSignature))
        {
            return "image/png";
        }

        if (header.Length >= 3 && header[0] == 0xff && header[1] == 0xd8 && header[2] == 0xff)
        {
            return "image/jpeg";
        }

        if (header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        return null;
    }
}
