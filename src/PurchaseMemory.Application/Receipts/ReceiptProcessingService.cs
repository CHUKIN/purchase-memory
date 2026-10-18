using PurchaseMemory.Application.Purchases;
using PurchaseMemory.Application.Receipts.TextLocation;
using PurchaseMemory.Domain.Receipts;

namespace PurchaseMemory.Application.Receipts;

public sealed class ReceiptProcessingService : IReceiptProcessingService
{
    private const decimal MoneyComparisonTolerance = 0.01m;

    private readonly IReceiptCaptureRepository _receiptRepository;
    private readonly IReceiptImageStore _imageStore;
    private readonly IReceiptExtractor _extractor;
    private readonly IReceiptProcessingDispatcher _processingDispatcher;
    private readonly IPurchaseDraftService _purchaseDraftService;
    private readonly TimeProvider _timeProvider;
    private readonly IReceiptTextHighlighter? _textHighlighter;
    private readonly IReceiptTextLocationSignal _textLocationSignal;

    /// <param name="textHighlighter">Re-matches printed-text outlines after a review; optional (tools, tests).</param>
    /// <param name="textLocationSignal">Wakes the background text location once a receipt is recognised.</param>
    public ReceiptProcessingService(
        IReceiptCaptureRepository receiptRepository,
        IReceiptImageStore imageStore,
        IReceiptExtractor extractor,
        IReceiptProcessingDispatcher processingDispatcher,
        IPurchaseDraftService purchaseDraftService,
        TimeProvider timeProvider,
        IReceiptTextHighlighter? textHighlighter = null,
        IReceiptTextLocationSignal? textLocationSignal = null)
    {
        _receiptRepository = receiptRepository;
        _imageStore = imageStore;
        _extractor = extractor;
        _processingDispatcher = processingDispatcher;
        _purchaseDraftService = purchaseDraftService;
        _timeProvider = timeProvider;
        _textHighlighter = textHighlighter;
        _textLocationSignal = textLocationSignal ?? new NullReceiptTextLocationSignal();
    }

    public async Task<UploadReceiptResult> UploadAsync(
        UploadReceiptCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Images);
        if (command.Images.Count is < 1 or > ReceiptCaptureLimits.MaximumPartCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(command),
                $"A receipt must contain between 1 and {ReceiptCaptureLimits.MaximumPartCount} image parts.");
        }

        foreach (var image in command.Images)
        {
            ArgumentNullException.ThrowIfNull(image);
            ArgumentNullException.ThrowIfNull(image.Image);
            ArgumentException.ThrowIfNullOrWhiteSpace(image.DeclaredContentType);
            if (!image.Image.CanRead)
            {
                throw new ArgumentException("Every receipt image stream must be readable.", nameof(command));
            }
        }

        var receiptId = Guid.NewGuid();
        try
        {
            var storedImages = new List<StoredReceiptImage>(command.Images.Count);
            for (var position = 0; position < command.Images.Count; position++)
            {
                var image = command.Images[position];
                storedImages.Add(await _imageStore.StoreAsync(
                    receiptId,
                    position,
                    image.Image,
                    image.DeclaredContentType,
                    cancellationToken));
            }

            if (storedImages.Sum(image => image.SizeBytes) > ReceiptCaptureLimits.MaximumTotalSizeBytes)
            {
                throw new InvalidDataException(
                    $"Receipt images exceed the {ReceiptCaptureLimits.MaximumTotalSizeBytes / 1024 / 1024} MB total limit.");
            }

            var receipt = ReceiptCapture.CreateUploaded(
                receiptId,
                storedImages.Select((image, position) => ReceiptImagePart.Create(
                    position,
                    image.StorageKey,
                    image.ContentType,
                    image.SizeBytes,
                    image.Sha256)).ToList(),
                _timeProvider.GetUtcNow(),
                command.Actor?.UserId,
                command.Actor?.Email);

            var duplicate = await _receiptRepository.FindByImageSetSha256Async(
                receipt.ImageSha256,
                cancellationToken);

            if (duplicate is not null)
            {
                await _imageStore.DeleteReceiptAsync(receiptId, cancellationToken);
                return new UploadReceiptResult(CreateView(duplicate), true);
            }

            var racedDuplicate = await _receiptRepository.AddOrFindExactDuplicateAsync(
                receipt,
                cancellationToken);
            if (racedDuplicate is not null)
            {
                await _imageStore.DeleteReceiptAsync(receiptId, cancellationToken);
                return new UploadReceiptResult(CreateView(racedDuplicate), true);
            }

            return new UploadReceiptResult(CreateView(receipt), false);
        }
        catch
        {
            await _imageStore.DeleteReceiptAsync(receiptId, CancellationToken.None);
            throw;
        }
    }

    public async Task<ReceiptCaptureView> QueueAsync(
        Guid receiptId,
        CancellationToken cancellationToken)
    {
        var receipt = await FindReceiptAsync(receiptId, cancellationToken);

        if (receipt.Status is ReceiptProcessingStatus.Uploaded or ReceiptProcessingStatus.Failed)
        {
            receipt.Queue(_timeProvider.GetUtcNow());
            await _receiptRepository.SaveChangesAsync(cancellationToken);
        }

        if (receipt.Status == ReceiptProcessingStatus.Queued)
        {
            // Re-enqueuing is intentional: an application restart can leave a
            // durable Queued record after the in-memory queue was lost.
            await _processingDispatcher.EnqueueAsync(receipt.Id, cancellationToken);
        }

        return CreateView(receipt);
    }

    public async Task<ReceiptCaptureView> ProcessAsync(
        Guid receiptId,
        CancellationToken cancellationToken)
    {
        var receipt = await FindReceiptAsync(receiptId, cancellationToken);
        if (receipt.Status != ReceiptProcessingStatus.Queued)
        {
            // Duplicate queue messages are harmless and expected after retries.
            return CreateView(receipt);
        }

        receipt.StartProcessing(_timeProvider.GetUtcNow());
        await _receiptRepository.SaveChangesAsync(cancellationToken);

        try
        {
            var images = new List<ReceiptImageData>(receipt.ImageParts.Count);
            foreach (var part in receipt.ImageParts.OrderBy(part => part.Position))
            {
                var image = await _imageStore.OpenReadAsync(
                    part.StorageKey,
                    ReceiptImagePurpose.Analysis,
                    cancellationToken);
                await using var content = image.Content;
                await using var buffer = new MemoryStream(capacity: checked((int)part.SizeBytes));
                await content.CopyToAsync(buffer, cancellationToken);
                images.Add(new ReceiptImageData(buffer.ToArray(), image.ContentType ?? part.ContentType));
            }

            var extraction = await _extractor.ExtractAsync(images, cancellationToken);

            ArgumentNullException.ThrowIfNull(extraction);
            ArgumentNullException.ThrowIfNull(extraction.Items);

            receipt.CompleteProcessing(
                extraction.MerchantName,
                extraction.PurchasedAt,
                extraction.Currency,
                extraction.ReceiptTotal,
                CreateItems(extraction.Items),
                extraction.ExtractorName,
                extraction.ModelName,
                _timeProvider.GetUtcNow(),
                extraction.ReceiptTotalBoundingBox);

            await _receiptRepository.SaveChangesAsync(cancellationToken);
            _textLocationSignal.Notify();
            return CreateView(receipt);
        }
        catch (ReceiptExtractionException exception)
        {
            receipt.FailProcessing(exception.Message, _timeProvider.GetUtcNow());
            await _receiptRepository.SaveChangesAsync(CancellationToken.None);
            return CreateView(receipt);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            receipt.FailProcessing(
                "Receipt recognition failed unexpectedly. You can retry processing.",
                _timeProvider.GetUtcNow());
            await _receiptRepository.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<ReceiptCaptureView> GetAsync(
        Guid receiptId,
        CancellationToken cancellationToken)
    {
        var receipt = await FindReceiptAsync(receiptId, cancellationToken);
        return CreateView(receipt);
    }

    public async Task<IReadOnlyList<ReceiptReviewAuditEntryView>> GetAuditTrailAsync(
        Guid receiptId,
        CancellationToken cancellationToken)
    {
        var receipt = await FindReceiptAsync(receiptId, cancellationToken);
        return receipt.ReviewAuditEntries
            .OrderByDescending(entry => entry.OccurredAt)
            .Select(entry => new ReceiptReviewAuditEntryView(
                entry.OccurredAt,
                entry.Actor,
                entry.ActorEmail,
                entry.FieldName,
                entry.ChangeType,
                entry.OldValue,
                entry.NewValue))
            .ToList();
    }

    public async Task<IReadOnlyList<ReceiptCaptureView>> ListRecentAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        if (limit is < 1 or > ReceiptCaptureLimits.MaximumRecentCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit),
                $"Recent receipt limit must be between 1 and {ReceiptCaptureLimits.MaximumRecentCount}.");
        }

        var receipts = await _receiptRepository.ListRecentAsync(limit, cancellationToken);
        return receipts.Select(CreateView).ToList();
    }

    public async Task<IReadOnlyList<ReceiptCaptureView>> ListUnsavedUploadsAsync(
        string uploaderUserId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uploaderUserId);
        var since = _timeProvider.GetUtcNow() - ReceiptCaptureLimits.UnsavedUploadsWindow;
        var receipts = await _receiptRepository.ListUnsavedUploadedByAsync(
            uploaderUserId,
            since,
            ReceiptCaptureLimits.MaximumUnsavedUploadsCount,
            cancellationToken);
        return receipts.Select(CreateView).ToList();
    }

    public async Task<int> RecoverPendingAsync(CancellationToken cancellationToken)
    {
        var pending = await _receiptRepository.ListPendingAsync(cancellationToken);
        foreach (var receipt in pending)
        {
            receipt.RecoverAfterInterruption(_timeProvider.GetUtcNow());
        }

        await _receiptRepository.SaveChangesAsync(cancellationToken);
        foreach (var receipt in pending)
        {
            await _processingDispatcher.EnqueueAsync(receipt.Id, cancellationToken);
        }

        return pending.Count;
    }

    public async Task<ReceiptCaptureView> ReviewAsync(
        ReviewReceiptCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Items);

        var receipt = await FindReceiptAsync(command.ReceiptId, cancellationToken);
        EnsureStatus(receipt, ReceiptProcessingStatus.ReadyForReview, "review");

        var reviewedItems = CreateItems(command.Items);
        var now = _timeProvider.GetUtcNow();

        var auditEntries = ReceiptReviewAuditDiffer.Diff(
            receipt,
            command.MerchantName,
            command.PurchasedAt,
            command.Currency,
            command.ReceiptTotal,
            reviewedItems,
            command.Actor ?? ReceiptActor.Unattributed,
            now);

        receipt.ApplyReview(
            command.MerchantName,
            command.PurchasedAt,
            command.Currency,
            command.ReceiptTotal,
            reviewedItems,
            now,
            command.ReceiptTotalBoundingBox);
        if (_textHighlighter is not null)
        {
            await _textHighlighter.RefreshAsync(receipt, cancellationToken);
        }

        if (auditEntries.Count > 0)
        {
            receipt.AppendReviewAuditEntries(auditEntries);
        }

        await _receiptRepository.SaveChangesAsync(cancellationToken);
        return CreateView(receipt);
    }

    public async Task<PurchaseDraftView> SaveAsync(
        SaveReceiptCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var receipt = await FindReceiptAsync(command.ReceiptId, cancellationToken);
        if (receipt.Status == ReceiptProcessingStatus.Saved)
        {
            var existing = await _purchaseDraftService.FindBySourceReceiptIdAsync(
                receipt.Id,
                cancellationToken);

            return existing
                ?? throw new SavedReceiptInconsistencyException(
                    receipt.Id,
                    receipt.SavedPurchaseId!.Value);
        }

        EnsureStatus(receipt, ReceiptProcessingStatus.ReadyForReview, "save");

        var purchase = await _purchaseDraftService.CreateAsync(
            new CreatePurchaseDraftCommand(
                receipt.MerchantName,
                receipt.PurchasedAt,
                receipt.Currency,
                receipt.ReceiptTotal,
                receipt.Items
                    .OrderBy(item => item.Position)
                    .Select(item => new PurchaseItemCommand(
                        item.Name,
                        item.Quantity,
                        item.Unit,
                        item.UnitPrice,
                        item.LineTotal,
                        item.Kind))
                    .ToList(),
                command.AllowPossibleDuplicate,
                receipt.Id),
            cancellationToken);

        receipt.MarkSaved(purchase.PurchaseId, _timeProvider.GetUtcNow());
        await _receiptRepository.SaveChangesAsync(cancellationToken);
        return purchase;
    }

    private async Task<ReceiptCapture> FindReceiptAsync(
        Guid receiptId,
        CancellationToken cancellationToken)
    {
        if (receiptId == Guid.Empty)
        {
            throw new ArgumentException("Receipt identifier cannot be empty.", nameof(receiptId));
        }

        return await _receiptRepository.FindByIdAsync(receiptId, cancellationToken)
            ?? throw new ReceiptCaptureNotFoundException(receiptId);
    }

    private static List<ReceiptCaptureItem> CreateItems(
        IReadOnlyList<ReceiptItemCommand> commands)
    {
        return CreateItems(commands
            .Select(item => new ReceiptExtractionItem(
                item.Name,
                item.Quantity,
                item.Unit,
                item.UnitPrice,
                item.LineTotal,
                item.BoundingBox,
                ReceiptLineKindResolver.Resolve(item.Kind, item.LineTotal)))
            .ToList());
    }

    private static List<ReceiptCaptureItem> CreateItems(
        IReadOnlyList<ReceiptExtractionItem> items)
    {
        var links = ReceiptDiscountLinking.Resolve(items
            .Select(item => new ReceiptLineForLinking(item.Kind, item.Name, item.LineTotal))
            .ToList());
        return items
            .Select((item, position) => ReceiptCaptureItem.Create(
                position,
                item.Name,
                item.Quantity,
                item.Unit,
                item.UnitPrice,
                item.LineTotal,
                item.BoundingBox,
                item.Kind,
                links[position].TargetPosition))
            .ToList();
    }

    private static void EnsureStatus(
        ReceiptCapture receipt,
        ReceiptProcessingStatus requiredStatus,
        string requestedOperation)
    {
        if (receipt.Status != requiredStatus)
        {
            throw new ReceiptProcessingStateException(receipt.Id, receipt.Status, requestedOperation);
        }
    }

    private static ReceiptCaptureView CreateView(ReceiptCapture receipt)
    {
        var items = receipt.Items
            .OrderBy(item => item.Position)
            .Select(item => new ReceiptCaptureItemView(
                item.Name,
                item.Quantity,
                item.Unit,
                item.UnitPrice,
                item.LineTotal,
                item.BoundingBox,
                item.Kind,
                item.DiscountedItemPosition,
                item.TextPolygons.Count == 0 ? null : item.TextPolygons,
                item.BoxSource))
            .ToList();
        var imageParts = receipt.ImageParts
            .OrderBy(part => part.Position)
            .Select(part => new ReceiptImagePartView(
                part.Position,
                part.ContentType,
                part.StorageKey,
                part.SizeBytes,
                part.Sha256))
            .ToList();

        return new ReceiptCaptureView(
            receipt.Id,
            receipt.Status,
            receipt.ImageContentType,
            receipt.ImageStorageKey,
            receipt.ImageSizeBytes,
            receipt.ImageSha256,
            receipt.MerchantName,
            receipt.PurchasedAt,
            receipt.Currency,
            receipt.ReceiptTotal,
            receipt.ReceiptTotalBoundingBox,
            receipt.ExtractorName,
            receipt.ExtractorModel,
            receipt.ErrorMessage,
            receipt.SavedPurchaseId,
            receipt.CreatedAt,
            receipt.UpdatedAt,
            imageParts,
            items,
            CreateWarnings(
                receipt.MerchantName,
                receipt.PurchasedAt,
                receipt.Currency,
                receipt.ReceiptTotal,
                items),
            PurchaseDraftIssueDetector.Detect(
                receipt.ReceiptTotal,
                receipt.Currency,
                items
                    .Select((item, position) => new PurchaseDraftIssueItem(
                        position,
                        item.Name,
                        item.UnitPrice,
                        item.LineTotal,
                        item.BoundingBox,
                        item.Kind,
                        item.TextPolygons))
                    .ToList(),
                receipt.ReceiptTotalBoundingBox),
            receipt.UploadedByUserId,
            receipt.UploadedByEmail);
    }

    private static List<string> CreateWarnings(
        string? merchantName,
        DateTimeOffset? purchasedAt,
        string? currency,
        decimal? receiptTotal,
        List<ReceiptCaptureItemView> items)
    {
        var warnings = new List<string>();
        if (string.IsNullOrWhiteSpace(merchantName))
        {
            warnings.Add("Магазин не распознан.");
        }

        if (purchasedAt is null)
        {
            warnings.Add("Дата покупки не распознана.");
        }

        if (string.IsNullOrWhiteSpace(currency))
        {
            warnings.Add("Валюта не распознана.");
        }

        if (receiptTotal is null)
        {
            warnings.Add("Итог чека не распознан.");
        }

        if (items.All(item => item.Kind == ReceiptLineKind.Discount))
        {
            warnings.Add("Не найдено ни одной товарной позиции.");
        }

        // A receipt-level discount has no product by design; only a product
        // discount that lost its product needs a person to look at it.
        var links = ReceiptDiscountLinking.Resolve(items
            .Select(item => new ReceiptLineForLinking(item.Kind, item.Name, item.LineTotal))
            .ToList());
        foreach (var discount in items.Where((item, position) =>
            item.Kind == ReceiptLineKind.Discount
            && item.DiscountedItemPosition is null
            && !links[position].AppliesToReceipt))
        {
            warnings.Add($"Скидка «{discount.Name}» не относится ни к одному товару: над ней нет товарной строки.");
        }

        var missingLineTotals = items.Count(item => item.LineTotal is null);
        if (missingLineTotals > 0)
        {
            warnings.Add($"У {missingLineTotals} позиций не распознана сумма строки.");
        }
        else if (receiptTotal is not null && items.Count > 0)
        {
            // Discount lines carry negative totals, so the plain sum already
            // nets every discount off its product.
            var linesTotal = items.Sum(item => item.LineTotal!.Value);
            if (Math.Abs(linesTotal - receiptTotal.Value) > MoneyComparisonTolerance)
            {
                warnings.Add(
                    $"Сумма строк ({linesTotal:0.00}) не совпадает с итогом чека ({receiptTotal:0.00}).");
            }
        }

        return warnings;
    }
}
