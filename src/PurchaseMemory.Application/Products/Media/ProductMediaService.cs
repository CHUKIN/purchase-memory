using Microsoft.Extensions.Logging;
using PurchaseMemory.Application.Products.Categories;
using PurchaseMemory.Application.Products.PhotoNames;
using PurchaseMemory.Application.Receipts;
using PurchaseMemory.Domain.Products;

namespace PurchaseMemory.Application.Products.Media;

/// <summary>
/// Photos, barcodes, the «Больше не покупать» mark and remembered codes of the
/// household's products. Every change to a product leaves a product audit
/// entry. Photo bytes are written before their row and removed again when the
/// row cannot be saved, so a failed upload leaves nothing behind; a deleted
/// photo's row goes first, so a storage hiccup can only leave an unreachable
/// file, never a row without one.
/// </summary>
public sealed partial class ProductMediaService(
    IProductMediaRepository repository,
    IProductPhotoStore photos,
    TimeProvider timeProvider,
    ILogger<ProductMediaService> logger) : IProductMediaService
{
    public const string AvoidValue = "Avoid";

    public async Task<ProductMediaView> GetAsync(Guid productId, CancellationToken cancellationToken)
    {
        var product = await repository.FindProductForUpdateAsync(productId, cancellationToken)
            ?? throw new ProductNotFoundException(productId);
        return await ViewAsync(product, cancellationToken);
    }

    public async Task<ProductPhotoUploadResult> AddPhotoAsync(
        Guid productId,
        ReadOnlyMemory<byte> photo,
        string? clientId,
        ReceiptActor actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        clientId = string.IsNullOrWhiteSpace(clientId) ? null : clientId.Trim();
        if (clientId?.Length > ProductPhoto.MaximumClientIdLength)
        {
            throw new ArgumentException($"clientId cannot exceed {ProductPhoto.MaximumClientIdLength} characters.", nameof(clientId));
        }

        if (!await repository.ProductExistsAsync(productId, cancellationToken))
        {
            throw new ProductNotFoundException(productId);
        }

        if (clientId is not null && await repository.FindPhotoByClientIdAsync(productId, clientId, cancellationToken) is { } existing)
        {
            return new ProductPhotoUploadResult(ToView(existing), Created: false, FirstPhoto: false);
        }

        var image = ProductPhotoRules.Check(photo);
        var current = await repository.ListPhotosForUpdateAsync(productId, cancellationToken);
        if (current.Count >= ProductPhoto.MaximumPerProduct)
        {
            throw new ProductMediaLimitException("photoLimit", $"A product can have at most {ProductPhoto.MaximumPerProduct} photos.");
        }

        var photoId = Guid.NewGuid();
        var files = await photos.StoreAsync(photoId, image, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var first = current.Count == 0;
        var entry = ProductPhoto.Create(photoId, productId, files, isMain: first, clientId, actor.UserId, actor.Email, now);
        repository.AddPhoto(entry);
        Audit(productId, actor, ProductAuditEntry.PhotoField, null, photoId.ToString("D"), now);
        try
        {
            await repository.SaveChangesAsync(cancellationToken);
        }
        catch (ProductMediaSaveException)
        {
            // The product vanished (merged away) or the same client photo was
            // uploaded twice at once: the bytes just written belong to nothing.
            repository.DiscardChanges();
            await DeleteFilesQuietlyAsync(photoId);
            if (clientId is not null && await repository.FindPhotoByClientIdAsync(productId, clientId, cancellationToken) is { } raced)
            {
                return new ProductPhotoUploadResult(ToView(raced), Created: false, FirstPhoto: false);
            }

            if (!await repository.ProductExistsAsync(productId, cancellationToken))
            {
                throw new ProductNotFoundException(productId);
            }

            throw;
        }

        PhotoAdded(productId, photoId, files.SizeBytes, files.ThumbnailSizeBytes);
        return new ProductPhotoUploadResult(ToView(entry), Created: true, FirstPhoto: first);
    }

    public async Task RemovePhotoAsync(Guid productId, Guid photoId, ReceiptActor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!await repository.ProductExistsAsync(productId, cancellationToken))
        {
            throw new ProductNotFoundException(productId);
        }

        var all = await repository.ListPhotosForUpdateAsync(productId, cancellationToken);
        var photo = all.FirstOrDefault(entry => entry.Id == photoId) ?? throw new ProductPhotoNotFoundException(photoId);
        var now = timeProvider.GetUtcNow();
        repository.RemovePhoto(photo);
        Audit(productId, actor, ProductAuditEntry.PhotoField, photoId.ToString("D"), null, now);
        if (photo.IsMain && all.FirstOrDefault(entry => entry.Id != photoId) is { } next)
        {
            next.SetMain(true);
            Audit(productId, actor, ProductAuditEntry.MainPhotoField, photoId.ToString("D"), next.Id.ToString("D"), now);
        }

        await repository.SaveChangesAsync(cancellationToken);
        await DeleteFilesQuietlyAsync(photoId);
    }

    public async Task<ProductMediaView> SetMainPhotoAsync(Guid productId, Guid photoId, ReceiptActor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var product = await repository.FindProductForUpdateAsync(productId, cancellationToken)
            ?? throw new ProductNotFoundException(productId);
        var all = await repository.ListPhotosForUpdateAsync(productId, cancellationToken);
        var chosen = all.FirstOrDefault(entry => entry.Id == photoId) ?? throw new ProductPhotoNotFoundException(photoId);
        var previous = all.FirstOrDefault(entry => entry.IsMain);
        if (previous?.Id != chosen.Id || all.Count(entry => entry.IsMain) != 1)
        {
            foreach (var entry in all)
            {
                entry.SetMain(entry.Id == chosen.Id);
            }

            Audit(productId, actor, ProductAuditEntry.MainPhotoField, previous?.Id.ToString("D"), chosen.Id.ToString("D"), timeProvider.GetUtcNow());
            await repository.SaveChangesAsync(cancellationToken);
        }

        return await ViewAsync(product, cancellationToken);
    }

    public async Task<ProductPhotoContent?> OpenPhotoAsync(Guid photoId, bool thumbnail, CancellationToken cancellationToken)
    {
        var photo = await repository.FindPhotoAsync(photoId, cancellationToken);
        if (photo is null)
        {
            return null;
        }

        var stream = await photos.TryOpenReadAsync(thumbnail ? photo.ThumbnailKey : photo.StorageKey, cancellationToken);
        return stream is null ? null : new ProductPhotoContent(stream, "image/jpeg");
    }

    public async Task<ProductBarcodeAddResult> AddBarcodeAsync(
        Guid productId,
        string code,
        ProductBarcodeSource source,
        ReceiptActor actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var normalized = Barcode.TryNormalize(code) ?? throw new InvalidBarcodeException();
        if (!Enum.IsDefined(source))
        {
            throw new ArgumentException("source must be scan, manual or openFoodFacts.", nameof(source));
        }

        if (!await repository.ProductExistsAsync(productId, cancellationToken))
        {
            throw new ProductNotFoundException(productId);
        }

        if (await repository.FindBarcodeAsync(normalized, cancellationToken) is { } owner)
        {
            return owner.ProductId == productId
                ? new ProductBarcodeAddResult(ToView(owner), Created: false)
                : throw new BarcodeTakenException(normalized, owner.ProductId);
        }

        var current = await repository.ListBarcodesForUpdateAsync(productId, cancellationToken);
        if (current.Count >= ProductBarcode.MaximumPerProduct)
        {
            throw new ProductMediaLimitException("barcodeLimit", $"A product can have at most {ProductBarcode.MaximumPerProduct} barcodes.");
        }

        var now = timeProvider.GetUtcNow();
        var barcode = ProductBarcode.Create(productId, normalized, source, actor.UserId, actor.Email, now);
        repository.AddBarcode(barcode);
        if (await repository.FindRememberedForUpdateAsync(normalized, cancellationToken) is { } remembered)
        {
            repository.RemoveRemembered(remembered);
        }

        Audit(productId, actor, ProductAuditEntry.BarcodeField, null, $"{normalized} ({SourceText(source)})", now);
        try
        {
            await repository.SaveChangesAsync(cancellationToken);
        }
        catch (ProductMediaSaveException)
        {
            // Two people tied the same code at once: the unique index decided.
            repository.DiscardChanges();
            if (await repository.FindBarcodeAsync(normalized, cancellationToken) is { } winner)
            {
                return winner.ProductId == productId
                    ? new ProductBarcodeAddResult(ToView(winner), Created: false)
                    : throw new BarcodeTakenException(normalized, winner.ProductId);
            }

            if (!await repository.ProductExistsAsync(productId, cancellationToken))
            {
                throw new ProductNotFoundException(productId);
            }

            throw;
        }

        return new ProductBarcodeAddResult(ToView(barcode), Created: true);
    }

    public async Task RemoveBarcodeAsync(Guid productId, string code, ReceiptActor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var normalized = Barcode.TryNormalize(code) ?? throw new InvalidBarcodeException();
        if (!await repository.ProductExistsAsync(productId, cancellationToken))
        {
            throw new ProductNotFoundException(productId);
        }

        var barcode = (await repository.ListBarcodesForUpdateAsync(productId, cancellationToken))
            .FirstOrDefault(entry => entry.Code == normalized)
            ?? throw new ProductBarcodeNotFoundException(normalized);
        repository.RemoveBarcode(barcode);
        Audit(productId, actor, ProductAuditEntry.BarcodeField, normalized, null, timeProvider.GetUtcNow());
        await repository.SaveChangesAsync(cancellationToken);
    }

    public async Task<BarcodeLookup> FindByBarcodeAsync(string code, CancellationToken cancellationToken)
    {
        var normalized = Barcode.TryNormalize(code) ?? throw new InvalidBarcodeException();
        var owner = await repository.FindBarcodeAsync(normalized, cancellationToken);
        var remembered = owner is null ? await repository.FindRememberedForUpdateAsync(normalized, cancellationToken) : null;
        return new BarcodeLookup(
            normalized,
            owner is null ? [] : [owner.ProductId],
            remembered is null ? null : ToView(remembered));
    }

    public async Task<ProductMediaView> SetAvoidAsync(
        Guid productId,
        bool avoid,
        DateTimeOffset? since,
        ReceiptActor actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var product = await repository.FindProductForUpdateAsync(productId, cancellationToken)
            ?? throw new ProductNotFoundException(productId);
        var now = timeProvider.GetUtcNow();
        // A date from a phone is kept only when it is plausible: not in the future.
        var markedAt = since is { } given && given <= now && given.Year >= 2020 ? given : now;
        if (product.SetAvoid(avoid, markedAt, now))
        {
            Audit(productId, actor, ProductAuditEntry.AvoidField, avoid ? null : AvoidValue, avoid ? AvoidValue : null, now);
            await repository.SaveChangesAsync(cancellationToken);
        }

        return await ViewAsync(product, cancellationToken);
    }

    public async Task<IReadOnlyList<RememberedBarcodeView>> ListRememberedAsync(CancellationToken cancellationToken) =>
        (await repository.ListRememberedAsync(RememberedBarcode.MaximumCount, cancellationToken)).Select(ToView).ToList();

    public async Task<RememberedBarcodeView> RememberAsync(
        string code,
        OpenFoodFactsProduct? off,
        ReceiptActor actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var normalized = Barcode.TryNormalize(code) ?? throw new InvalidBarcodeException();
        if (await repository.FindBarcodeAsync(normalized, cancellationToken) is { } owner)
        {
            throw new BarcodeTakenException(normalized, owner.ProductId);
        }

        var hint = off is null ? null : OpenFoodFactsProduct.Clean(off.Name, off.Brand, off.Quantity, off.ImageUrl);
        var remembered = await repository.FindRememberedForUpdateAsync(normalized, cancellationToken);
        if (remembered is null)
        {
            remembered = RememberedBarcode.Create(normalized);
            repository.AddRemembered(remembered);
        }

        remembered.Remember(actor.UserId, timeProvider.GetUtcNow(), hint?.Name, hint?.Brand, hint?.Quantity, hint?.ImageUrl);
        try
        {
            await repository.SaveChangesAsync(cancellationToken);
        }
        catch (ProductMediaSaveException)
        {
            // Both members remembered the same code at the same moment; one row is what both wanted.
            repository.DiscardChanges();
            var stored = await repository.FindRememberedForUpdateAsync(normalized, cancellationToken);
            return stored is null ? throw new ProductMediaSaveException(new InvalidOperationException("Remembered code vanished.")) : ToView(stored);
        }

        await repository.TrimRememberedAsync(RememberedBarcode.MaximumCount, cancellationToken);
        return ToView(remembered);
    }

    public async Task ForgetAsync(string code, CancellationToken cancellationToken)
    {
        var normalized = Barcode.TryNormalize(code) ?? throw new InvalidBarcodeException();
        if (await repository.FindRememberedForUpdateAsync(normalized, cancellationToken) is { } remembered)
        {
            repository.RemoveRemembered(remembered);
            await repository.SaveChangesAsync(cancellationToken);
        }
    }

    public Task<IReadOnlyDictionary<Guid, Guid>> MainPhotoIdsAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(productIds);
        return productIds.Count == 0
            ? Task.FromResult<IReadOnlyDictionary<Guid, Guid>>(new Dictionary<Guid, Guid>())
            : repository.MainPhotoIdsAsync(productIds, cancellationToken);
    }

    private async Task<ProductMediaView> ViewAsync(Product product, CancellationToken cancellationToken)
    {
        var photoRows = await repository.ListPhotosForUpdateAsync(product.Id, cancellationToken);
        var barcodeRows = await repository.ListBarcodesForUpdateAsync(product.Id, cancellationToken);
        var main = photoRows.FirstOrDefault(entry => entry.IsMain) ?? (photoRows.Count > 0 ? photoRows[0] : null);
        return new ProductMediaView(
            product.Id,
            photoRows.Select(ToView).ToList(),
            main?.Id,
            barcodeRows.Select(ToView).ToList(),
            product.AvoidSince);
    }

    private async Task DeleteFilesQuietlyAsync(Guid photoId)
    {
        try
        {
            // Not the request's token: a cancelled request must not leave the bytes behind.
            await photos.DeleteAsync(photoId, CancellationToken.None);
        }
#pragma warning disable CA1031 // The row is gone or was never saved; an orphan file only costs space.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            PhotoFilesNotDeleted(photoId, exception.GetType().Name);
        }
    }

    private void Audit(Guid productId, ReceiptActor actor, string field, string? oldValue, string? newValue, DateTimeOffset now)
    {
        repository.AddAuditEntry(ProductAuditEntry.Create(
            productId, actor.UserId, actor.Email, field, ProductAuditChangeSource.Human, oldValue, newValue, now));
    }

    private static ProductPhotoView ToView(ProductPhoto photo) =>
        new(photo.Id, photo.CreatedAt, photo.Width, photo.Height, photo.SizeBytes, photo.IsMain);

    private static ProductBarcodeView ToView(ProductBarcode barcode) =>
        new(barcode.Code, barcode.Source, barcode.CreatedAt);

    private static RememberedBarcodeView ToView(RememberedBarcode remembered) =>
        new(
            remembered.Code,
            remembered.RememberedAt,
            remembered.OffName is null && remembered.OffBrand is null
                ? null
                : new OpenFoodFactsProduct(remembered.OffName, remembered.OffBrand, remembered.OffQuantity, remembered.OffImageUrl));

    public static string SourceText(ProductBarcodeSource source) => source switch
    {
        ProductBarcodeSource.Manual => "manual",
        ProductBarcodeSource.OpenFoodFacts => "openFoodFacts",
        _ => "scan"
    };

    [LoggerMessage(EventId = 3501, Level = LogLevel.Information,
        Message = "Product {ProductId} got photo {PhotoId}: {StoredBytes} bytes, thumbnail {ThumbnailBytes} bytes")]
    private partial void PhotoAdded(Guid productId, Guid photoId, long storedBytes, long thumbnailBytes);

    [LoggerMessage(EventId = 3502, Level = LogLevel.Warning,
        Message = "Files of product photo {PhotoId} were not deleted ({ErrorType}); they are unreachable now")]
    private partial void PhotoFilesNotDeleted(Guid photoId, string errorType);
}
