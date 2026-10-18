using PurchaseMemory.Application.Purchases;
using PurchaseMemory.Domain.Receipts;

namespace PurchaseMemory.Application.Receipts;

public sealed record ReceiptCaptureItemView(
    string Name,
    decimal? Quantity,
    string? Unit,
    decimal? UnitPrice,
    decimal? LineTotal,
    BoundingBox? BoundingBox = null,
    ReceiptLineKind Kind = ReceiptLineKind.Product,
    int? DiscountedItemPosition = null,
    IReadOnlyList<TextPolygon>? TextPolygons = null,
    ReceiptBoxSource BoxSource = ReceiptBoxSource.Recognition);

public sealed record ReceiptImagePartView(
    int Position,
    string ContentType,
    string StorageKey,
    long SizeBytes,
    string Sha256);

public sealed record ReceiptReviewAuditEntryView(
    DateTimeOffset OccurredAt,
    string Actor,
    string? ActorEmail,
    string FieldName,
    ReceiptReviewAuditChangeType ChangeType,
    string? OldValue,
    string? NewValue);

public sealed record UploadReceiptResult(
    ReceiptCaptureView Receipt,
    bool IsExactDuplicate);

public sealed record ReceiptCaptureView(
    Guid ReceiptId,
    ReceiptProcessingStatus Status,
    string ImageContentType,
    string ImageStorageKey,
    long ImageSizeBytes,
    string ImageSha256,
    string? MerchantName,
    DateTimeOffset? PurchasedAt,
    string? Currency,
    decimal? ReceiptTotal,
    BoundingBox? ReceiptTotalBoundingBox,
    string? ExtractorName,
    string? ExtractorModel,
    string? ErrorMessage,
    Guid? SavedPurchaseId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<ReceiptImagePartView> ImageParts,
    IReadOnlyList<ReceiptCaptureItemView> Items,
    // Warnings stay a flat list: they also cover unrecognized merchant, date and
    // currency, which this slice does not structure. Issues are the reviewable
    // subset that the issue-based review screen can act on field by field.
    IReadOnlyList<string> Warnings,
    IReadOnlyList<PurchaseDraftIssue> Issues,
    string? UploadedByUserId = null,
    string? UploadedByEmail = null);
