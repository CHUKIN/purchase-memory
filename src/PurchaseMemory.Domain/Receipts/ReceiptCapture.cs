namespace PurchaseMemory.Domain.Receipts;

/// <summary>
/// Tracks an uploaded receipt from durable image storage through recognition and review.
/// </summary>
public sealed class ReceiptCapture
{
    private readonly List<ReceiptCaptureItem> _items = [];
    private readonly List<ReceiptImagePart> _imageParts = [];
    private readonly List<ReceiptReviewAuditEntry> _reviewAuditEntries = [];

    private ReceiptCapture()
    {
        // Required by Entity Framework Core.
    }

    private ReceiptCapture(
        Guid id,
        IReadOnlyList<ReceiptImagePart> imageParts,
        DateTimeOffset createdAt,
        string? uploadedByUserId,
        string? uploadedByEmail)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Receipt identifier cannot be empty.", nameof(id));
        }

        ArgumentNullException.ThrowIfNull(imageParts);
        if (imageParts.Count is < 1 or > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(imageParts), "A receipt must contain between 1 and 8 image parts.");
        }

        var orderedParts = imageParts.OrderBy(part => part.Position).ToList();
        if (orderedParts.Where((part, index) => part.Position != index).Any())
        {
            throw new ArgumentException("Receipt image part positions must be contiguous and start at zero.", nameof(imageParts));
        }

        Id = id;
        _imageParts.AddRange(orderedParts);

        // These columns are retained as a compatibility mirror so the additive
        // migration can preserve every existing single-image database safely.
        var firstPart = orderedParts[0];
        ImageStorageKey = firstPart.StorageKey;
        ImageContentType = firstPart.ContentType;
        ImageSizeBytes = imageParts.Sum(part => part.SizeBytes);
        ImageSha256 = ComputeImageSetSha256(orderedParts);
        Status = ReceiptProcessingStatus.Uploaded;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
        UploadedByUserId = NormalizeUploader(uploadedByUserId, 200);
        UploadedByEmail = NormalizeUploader(uploadedByEmail, 320);
    }

    public Guid Id { get; private set; }

    public string ImageStorageKey { get; private set; } = string.Empty;

    public string ImageContentType { get; private set; } = string.Empty;

    public long ImageSizeBytes { get; private set; }

    public string ImageSha256 { get; private set; } = string.Empty;

    public ReceiptProcessingStatus Status { get; private set; }

    public string? MerchantName { get; private set; }

    public DateTimeOffset? PurchasedAt { get; private set; }

    public string? Currency { get; private set; }

    public decimal? ReceiptTotal { get; private set; }

    /// <summary>Best-effort location of the printed total on the receipt photo.</summary>
    public BoundingBox? ReceiptTotalBoundingBox { get; private set; }

    public string? ExtractorName { get; private set; }

    public string? ExtractorModel { get; private set; }

    public string? ErrorMessage { get; private set; }

    public Guid? SavedPurchaseId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Who uploaded the photos: the Supabase user id and a readable email copy.
    /// Null for receipts captured before sign-in existed.
    /// </summary>
    public string? UploadedByUserId { get; private set; }

    public string? UploadedByEmail { get; private set; }

    /// <summary>Background text location (Azure word outlines); see <see cref="ReceiptTextLayout"/>.</summary>
    public ReceiptTextLocationStatus TextLocationStatus { get; private set; }

    /// <summary>Tries that count toward the retry limit (service outages are given back).</summary>
    public int TextLocationAttempts { get; private set; }

    /// <summary>Every call sent to the text-location service for this receipt (the monthly budget).</summary>
    public int TextLocationCalls { get; private set; }

    public DateTimeOffset? TextLocationAttemptedAt { get; private set; }

    public IReadOnlyCollection<ReceiptCaptureItem> Items => _items;

    public IReadOnlyCollection<ReceiptImagePart> ImageParts => _imageParts;

    /// <summary>Append-only audit trail of human corrections made during review.</summary>
    public IReadOnlyCollection<ReceiptReviewAuditEntry> ReviewAuditEntries => _reviewAuditEntries;

    public static ReceiptCapture CreateUploaded(
        Guid id,
        IReadOnlyList<ReceiptImagePart> imageParts,
        DateTimeOffset createdAt,
        string? uploadedByUserId = null,
        string? uploadedByEmail = null)
    {
        return new ReceiptCapture(id, imageParts, createdAt, uploadedByUserId, uploadedByEmail);
    }

    private static string? NormalizeUploader(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maximumLength)
        {
            throw new ArgumentException($"Uploader value cannot exceed {maximumLength} characters.", nameof(value));
        }

        return trimmed;
    }

    public void Queue(DateTimeOffset updatedAt)
    {
        EnsureTransitionTo(ReceiptProcessingStatus.Queued, ReceiptProcessingStatus.Uploaded, ReceiptProcessingStatus.Failed);
        Status = ReceiptProcessingStatus.Queued;
        ErrorMessage = null;
        UpdatedAt = updatedAt;
    }

    public void StartProcessing(DateTimeOffset updatedAt)
    {
        EnsureTransitionTo(ReceiptProcessingStatus.Processing, ReceiptProcessingStatus.Queued);
        Status = ReceiptProcessingStatus.Processing;
        ErrorMessage = null;
        UpdatedAt = updatedAt;
    }

    public void RecoverAfterInterruption(DateTimeOffset updatedAt)
    {
        if (Status == ReceiptProcessingStatus.Processing)
        {
            Status = ReceiptProcessingStatus.Queued;
            ErrorMessage = null;
            UpdatedAt = updatedAt;
        }
    }

    public void CompleteProcessing(
        string? merchantName,
        DateTimeOffset? purchasedAt,
        string? currency,
        decimal? receiptTotal,
        IEnumerable<ReceiptCaptureItem> items,
        string extractorName,
        string? extractorModel,
        DateTimeOffset updatedAt,
        BoundingBox? receiptTotalBoundingBox = null)
    {
        EnsureTransitionTo(ReceiptProcessingStatus.ReadyForReview, ReceiptProcessingStatus.Processing);
        ReplaceRecognizedDetails(merchantName, purchasedAt, currency, receiptTotal, items, receiptTotalBoundingBox);

        ExtractorName = NormalizeRequiredText(extractorName, nameof(extractorName), 100);
        ExtractorModel = NormalizeOptionalText(extractorModel, 200);
        ErrorMessage = null;
        Status = ReceiptProcessingStatus.ReadyForReview;
        UpdatedAt = updatedAt;
    }

    public void FailProcessing(string errorMessage, DateTimeOffset updatedAt)
    {
        EnsureTransitionTo(ReceiptProcessingStatus.Failed, ReceiptProcessingStatus.Processing);
        ErrorMessage = NormalizeRequiredText(errorMessage, nameof(errorMessage), 1000);
        Status = ReceiptProcessingStatus.Failed;
        UpdatedAt = updatedAt;
    }

    public void ApplyReview(
        string? merchantName,
        DateTimeOffset? purchasedAt,
        string? currency,
        decimal? receiptTotal,
        IEnumerable<ReceiptCaptureItem> items,
        DateTimeOffset updatedAt,
        BoundingBox? receiptTotalBoundingBox = null)
    {
        EnsureTransitionTo(ReceiptProcessingStatus.ReadyForReview, ReceiptProcessingStatus.ReadyForReview);
        ReplaceRecognizedDetails(merchantName, purchasedAt, currency, receiptTotal, items, receiptTotalBoundingBox);
        UpdatedAt = updatedAt;
    }

    /// <summary>
    /// Keeps the receipt snapshot in step with a correction of the purchase it
    /// was saved as, so the receipt page and its audit trail show what was
    /// fixed. Only a saved receipt can be corrected this way.
    /// </summary>
    public void ApplyCorrectionAfterSave(
        string? merchantName,
        DateTimeOffset? purchasedAt,
        string? currency,
        decimal? receiptTotal,
        IEnumerable<ReceiptCaptureItem> items,
        DateTimeOffset updatedAt)
    {
        EnsureTransitionTo(ReceiptProcessingStatus.Saved, ReceiptProcessingStatus.Saved);
        ReplaceRecognizedDetails(merchantName, purchasedAt, currency, receiptTotal, items, ReceiptTotalBoundingBox);
        UpdatedAt = updatedAt;
    }

    /// <summary>
    /// Records human corrections made during review. Append-only: entries are
    /// never mutated or removed by domain code.
    /// </summary>
    public void AppendReviewAuditEntries(IEnumerable<ReceiptReviewAuditEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _reviewAuditEntries.AddRange(entries);
    }

    public void MarkSaved(Guid purchaseId, DateTimeOffset updatedAt)
    {
        if (Status == ReceiptProcessingStatus.Saved && SavedPurchaseId == purchaseId)
        {
            return;
        }

        if (purchaseId == Guid.Empty)
        {
            throw new ArgumentException("Purchase identifier cannot be empty.", nameof(purchaseId));
        }

        EnsureTransitionTo(ReceiptProcessingStatus.Saved, ReceiptProcessingStatus.ReadyForReview);
        SavedPurchaseId = purchaseId;
        Status = ReceiptProcessingStatus.Saved;
        UpdatedAt = updatedAt;
    }

    /// <summary>
    /// Sets each line's printed-text outlines by position (lines missing from
    /// the map fall back to the recognition box). A background enrichment, so
    /// <see cref="UpdatedAt"/> is left alone.
    /// </summary>
    public void ApplyTextHighlights(IReadOnlyDictionary<int, IReadOnlyList<TextPolygon>> polygonsByPosition)
    {
        ArgumentNullException.ThrowIfNull(polygonsByPosition);
        foreach (var item in _items)
        {
            item.ShowPrintedText(polygonsByPosition.TryGetValue(item.Position, out var polygons) ? polygons : []);
        }
    }

    /// <summary>
    /// Recorded (and saved) before the service is called: the call counts
    /// toward the monthly budget and the retry limit, and the receipt reads as
    /// a passing failure until an outcome is recorded — so a crash, a malformed
    /// answer or a failed save afterwards can never cause an uncounted retry.
    /// </summary>
    public void BeginTextLocationCall(DateTimeOffset calledAt)
    {
        TextLocationCalls++;
        TextLocationAttempts++;
        TextLocationAttemptedAt = calledAt;
        TextLocationStatus = ReceiptTextLocationStatus.Failed;
    }

    /// <summary>The call returned a layout (stored separately).</summary>
    public void RecordTextLocated()
    {
        TextLocationStatus = ReceiptTextLocationStatus.Located;
    }

    /// <summary>
    /// The call (or its handling) failed. A passing failure is retried later, a
    /// lasting one (or the last allowed try) stops further calls for this receipt.
    /// </summary>
    public void RecordTextLocationFailed(bool lasting, int maximumAttempts)
    {
        TextLocationStatus = lasting || TextLocationAttempts >= maximumAttempts
            ? ReceiptTextLocationStatus.Unavailable
            : ReceiptTextLocationStatus.Failed;
    }

    /// <summary>
    /// The service was not usable at all (rate limit, refused key): not the
    /// receipt's fault, so the try is given back and it waits for its turn
    /// again. The call itself stays counted in <see cref="TextLocationCalls"/>.
    /// </summary>
    public void ReleaseTextLocationAttempt()
    {
        TextLocationAttempts = Math.Max(0, TextLocationAttempts - 1);
        TextLocationStatus = ReceiptTextLocationStatus.NotRequested;
    }

    /// <summary>The photo could not be read from storage: a try without a call.</summary>
    public void RecordTextLocationNotSent(DateTimeOffset attemptedAt, int maximumAttempts)
    {
        TextLocationAttempts++;
        TextLocationAttemptedAt = attemptedAt;
        RecordTextLocationFailed(lasting: false, maximumAttempts);
    }

    /// <summary>Never sent to the service (several photos, unusable photo); costs no call.</summary>
    public void MarkTextLocationUnavailable()
    {
        TextLocationStatus = ReceiptTextLocationStatus.Unavailable;
    }

    private void ReplaceRecognizedDetails(
        string? merchantName,
        DateTimeOffset? purchasedAt,
        string? currency,
        decimal? receiptTotal,
        IEnumerable<ReceiptCaptureItem> items,
        BoundingBox? receiptTotalBoundingBox)
    {
        ArgumentNullException.ThrowIfNull(items);

        MerchantName = NormalizeOptionalText(merchantName, 200);
        PurchasedAt = purchasedAt;
        Currency = NormalizeCurrency(currency);
        ReceiptTotal = EnsureNonNegative(receiptTotal, nameof(receiptTotal));
        ReceiptTotalBoundingBox = receiptTotalBoundingBox;

        _items.Clear();
        _items.AddRange(items.OrderBy(item => item.Position));
    }

    private void EnsureTransitionTo(
        ReceiptProcessingStatus targetStatus,
        params ReadOnlySpan<ReceiptProcessingStatus> allowedCurrentStatuses)
    {
        if (!allowedCurrentStatuses.Contains(Status))
        {
            throw new ReceiptStateTransitionException(Id, Status, targetStatus);
        }
    }

    private static decimal? EnsureNonNegative(decimal? value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Receipt total cannot be negative.");
        }

        return value;
    }

    private static string? NormalizeCurrency(string? currency)
    {
        if (string.IsNullOrWhiteSpace(currency))
        {
            return null;
        }

        var normalized = currency.Trim().ToUpperInvariant();
        if (normalized.Length != 3 || normalized.Any(character => !char.IsAsciiLetterUpper(character)))
        {
            throw new ArgumentException(
                "Currency must be a three-letter ISO 4217 code, for example PLN or EUR.",
                nameof(currency));
        }

        return normalized;
    }

    private static string NormalizeSha256(string value)
    {
        var normalized = NormalizeRequiredText(value, nameof(value), 64).ToLowerInvariant();
        if (normalized.Length != 64 || normalized.Any(character => !char.IsAsciiHexDigit(character)))
        {
            throw new ArgumentException("Image SHA-256 must contain exactly 64 hexadecimal characters.", nameof(value));
        }

        return normalized;
    }

    private static string ComputeImageSetSha256(List<ReceiptImagePart> parts)
    {
        if (parts.Count == 1)
        {
            return parts[0].Sha256;
        }

        var canonical = string.Join('|', parts.Select(part => $"{part.Position}:{part.Sha256}"));
        var bytes = System.Text.Encoding.ASCII.GetBytes(canonical);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
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

    private static string? NormalizeOptionalText(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException($"Value cannot exceed {maximumLength} characters.", nameof(value));
        }

        return normalized;
    }
}
