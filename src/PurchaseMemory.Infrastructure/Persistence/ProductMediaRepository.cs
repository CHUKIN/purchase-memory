using Microsoft.EntityFrameworkCore;
using PurchaseMemory.Application.Products.Media;
using PurchaseMemory.Domain.Products;

namespace PurchaseMemory.Infrastructure.Persistence;

/// <summary>Product photos, barcodes, the avoid mark and remembered codes (provider-neutral EF Core).</summary>
public sealed class ProductMediaRepository(PurchaseMemoryDbContext dbContext) : IProductMediaRepository
{
    public Task<Product?> FindProductForUpdateAsync(Guid productId, CancellationToken cancellationToken) =>
        dbContext.Products.FirstOrDefaultAsync(product => product.Id == productId, cancellationToken);

    public Task<bool> ProductExistsAsync(Guid productId, CancellationToken cancellationToken) =>
        dbContext.Products.AnyAsync(product => product.Id == productId, cancellationToken);

    public async Task<IReadOnlyList<ProductPhoto>> ListPhotosForUpdateAsync(Guid productId, CancellationToken cancellationToken) =>
        await dbContext.ProductPhotos
            .Where(photo => photo.ProductId == productId)
            // CreatedAt is stored as Unix milliseconds; the id settles two photos of the same millisecond.
            .OrderBy(photo => photo.CreatedAt)
            .ThenBy(photo => photo.Id)
            .ToListAsync(cancellationToken);

    public Task<ProductPhoto?> FindPhotoAsync(Guid photoId, CancellationToken cancellationToken) =>
        dbContext.ProductPhotos.AsNoTracking().FirstOrDefaultAsync(photo => photo.Id == photoId, cancellationToken);

    public Task<ProductPhoto?> FindPhotoByClientIdAsync(Guid productId, string clientId, CancellationToken cancellationToken) =>
        dbContext.ProductPhotos
            .AsNoTracking()
            .FirstOrDefaultAsync(photo => photo.ProductId == productId && photo.ClientId == clientId, cancellationToken);

    public async Task<IReadOnlyList<ProductBarcode>> ListBarcodesForUpdateAsync(Guid productId, CancellationToken cancellationToken) =>
        await dbContext.ProductBarcodes
            .Where(barcode => barcode.ProductId == productId)
            .OrderBy(barcode => barcode.CreatedAt)
            .ThenBy(barcode => barcode.Code)
            .ToListAsync(cancellationToken);

    public Task<ProductBarcode?> FindBarcodeAsync(string code, CancellationToken cancellationToken) =>
        dbContext.ProductBarcodes.AsNoTracking().FirstOrDefaultAsync(barcode => barcode.Code == code, cancellationToken);

    public void AddPhoto(ProductPhoto photo) => dbContext.ProductPhotos.Add(photo);

    public void RemovePhoto(ProductPhoto photo) => dbContext.ProductPhotos.Remove(photo);

    public void AddBarcode(ProductBarcode barcode) => dbContext.ProductBarcodes.Add(barcode);

    public void RemoveBarcode(ProductBarcode barcode) => dbContext.ProductBarcodes.Remove(barcode);

    public Task<RememberedBarcode?> FindRememberedForUpdateAsync(string code, CancellationToken cancellationToken) =>
        dbContext.RememberedBarcodes.FirstOrDefaultAsync(entry => entry.Code == code, cancellationToken);

    public async Task<IReadOnlyList<RememberedBarcode>> ListRememberedAsync(int limit, CancellationToken cancellationToken) =>
        await dbContext.RememberedBarcodes
            .AsNoTracking()
            .OrderByDescending(entry => entry.RememberedAt)
            .ThenBy(entry => entry.Code)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public void AddRemembered(RememberedBarcode remembered) => dbContext.RememberedBarcodes.Add(remembered);

    public void RemoveRemembered(RememberedBarcode remembered) => dbContext.RememberedBarcodes.Remove(remembered);

    public async Task TrimRememberedAsync(int keep, CancellationToken cancellationToken)
    {
        var count = await dbContext.RememberedBarcodes.CountAsync(cancellationToken);
        if (count <= keep)
        {
            return;
        }

        var oldest = await dbContext.RememberedBarcodes
            .AsNoTracking()
            .OrderBy(entry => entry.RememberedAt)
            .ThenByDescending(entry => entry.Code)
            .Take(count - keep)
            .Select(entry => entry.Code)
            .ToListAsync(cancellationToken);
        await dbContext.RememberedBarcodes
            .Where(entry => oldest.Contains(entry.Code))
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, Guid>> MainPhotoIdsAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken)
    {
        var ids = productIds.Distinct().ToList();
        var rows = await dbContext.ProductPhotos
            .AsNoTracking()
            .Where(photo => ids.Contains(photo.ProductId) && photo.IsMain)
            .Select(photo => new { photo.ProductId, photo.Id })
            .ToListAsync(cancellationToken);
        var result = new Dictionary<Guid, Guid>();
        foreach (var row in rows)
        {
            result.TryAdd(row.ProductId, row.Id);
        }

        return result;
    }

    public void AddAuditEntry(ProductAuditEntry entry) => dbContext.ProductAuditEntries.Add(entry);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            throw new ProductMediaSaveException(exception);
        }
    }

    public void DiscardChanges() => dbContext.ChangeTracker.Clear();
}
