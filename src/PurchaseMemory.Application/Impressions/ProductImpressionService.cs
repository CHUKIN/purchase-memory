using PurchaseMemory.Application.Receipts;
using PurchaseMemory.Domain.Impressions;

namespace PurchaseMemory.Application.Impressions;

public interface IProductImpressionService
{
    Task<ImpressionView> CreateAsync(CreateImpressionCommand command, ReceiptActor author, CancellationToken cancellationToken);

    Task<IReadOnlyList<ImpressionView>> ListForProductAsync(Guid productId, string viewerUserId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ImpressionView>> ListForPurchaseItemAsync(Guid purchaseItemId, string viewerUserId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ImpressionView>> ListForPurchaseAsync(Guid purchaseId, string viewerUserId, CancellationToken cancellationToken);

    Task<IReadOnlyList<RateablePurchaseItemView>> ListUnratedAsync(string viewerUserId, int? days, int? limit, CancellationToken cancellationToken);

    Task<IReadOnlyList<RateablePurchaseItemView>> ListProductLinesAsync(Guid productId, int? limit, CancellationToken cancellationToken);

    /// <summary>Soft delete; only the author may do it.</summary>
    Task DeleteAsync(Guid impressionId, ReceiptActor actor, CancellationToken cancellationToken);

    /// <summary>Null when the impression is gone, withdrawn or has no photo.</summary>
    Task<(Stream Content, string ContentType)?> OpenPhotoAsync(Guid impressionId, CancellationToken cancellationToken);
}

/// <summary>
/// Personal impressions over the shared purchase history. Everyone approved can
/// read every impression (the household compares opinions); only the author can
/// withdraw one. There is deliberately no edit: a changed opinion is a new
/// impression, so the history of opinions is never rewritten.
/// </summary>
public sealed class ProductImpressionService(
    IProductImpressionRepository repository,
    IImpressionPhotoStore photos,
    TimeProvider timeProvider) : IProductImpressionService
{
    public const int DefaultUnratedDays = 30;
    public const int MaximumUnratedDays = 180;
    public const int DefaultUnratedLimit = 8;
    public const int MaximumUnratedLimit = 50;

    public async Task<ImpressionView> CreateAsync(
        CreateImpressionCommand command,
        ReceiptActor author,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(author);
        if (command.PurchaseItemId == Guid.Empty)
        {
            throw new ArgumentException("A purchase line is required.", nameof(command));
        }

        if (!Enum.IsDefined(command.Rating))
        {
            throw new ArgumentException("Rating must be dislike, neutral or like.", nameof(command));
        }

        // Validated before any photo bytes are written.
        var note = ProductImpression.NormalizeNote(command.Note);
        var item = await repository.FindItemContextAsync(command.PurchaseItemId, cancellationToken)
            ?? throw new ImpressionPurchaseItemNotFoundException(command.PurchaseItemId);
        if (item.IsDiscount)
        {
            throw new ArgumentException("A discount line cannot be rated.", nameof(command));
        }

        var impressionId = Guid.NewGuid();
        StoredImpressionPhoto? photo = null;
        if (command.Photo is not null)
        {
            photo = await photos.StoreAsync(impressionId, command.Photo.Content, cancellationToken);
        }

        try
        {
            repository.Add(ProductImpression.Create(
                impressionId,
                item.PurchaseId,
                item.PurchaseItemId,
                item.ProductId,
                item.Name,
                author.UserId,
                author.Email,
                command.Rating,
                note,
                photo?.StorageKey,
                photo?.ContentType,
                timeProvider.GetUtcNow()));
            await repository.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            if (photo is not null)
            {
                await photos.DeleteAsync(photo.StorageKey, CancellationToken.None);
            }

            throw;
        }

        var views = await repository.ListAsync(new ImpressionFilter(ImpressionId: impressionId, Limit: 1), cancellationToken);
        return WithViewer(views, author.UserId).Single();
    }

    public async Task<IReadOnlyList<ImpressionView>> ListForProductAsync(
        Guid productId,
        string viewerUserId,
        CancellationToken cancellationToken) =>
        WithViewer(await repository.ListAsync(new ImpressionFilter(ProductId: productId), cancellationToken), viewerUserId);

    public async Task<IReadOnlyList<ImpressionView>> ListForPurchaseItemAsync(
        Guid purchaseItemId,
        string viewerUserId,
        CancellationToken cancellationToken) =>
        WithViewer(await repository.ListAsync(new ImpressionFilter(PurchaseItemId: purchaseItemId), cancellationToken), viewerUserId);

    public async Task<IReadOnlyList<ImpressionView>> ListForPurchaseAsync(
        Guid purchaseId,
        string viewerUserId,
        CancellationToken cancellationToken) =>
        WithViewer(await repository.ListAsync(new ImpressionFilter(PurchaseId: purchaseId), cancellationToken), viewerUserId);

    public Task<IReadOnlyList<RateablePurchaseItemView>> ListUnratedAsync(
        string viewerUserId,
        int? days,
        int? limit,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(viewerUserId);
        var window = days ?? DefaultUnratedDays;
        var count = limit ?? DefaultUnratedLimit;
        if (window is < 1 or > MaximumUnratedDays)
        {
            throw new ArgumentException($"days must be between 1 and {MaximumUnratedDays}.", nameof(days));
        }

        if (count is < 1 or > MaximumUnratedLimit)
        {
            throw new ArgumentException($"limit must be between 1 and {MaximumUnratedLimit}.", nameof(limit));
        }

        return repository.ListUnratedAsync(viewerUserId, timeProvider.GetUtcNow().AddDays(-window), count, cancellationToken);
    }

    public Task<IReadOnlyList<RateablePurchaseItemView>> ListProductLinesAsync(
        Guid productId,
        int? limit,
        CancellationToken cancellationToken)
    {
        var count = limit ?? MaximumUnratedLimit;
        if (count is < 1 or > MaximumUnratedLimit)
        {
            throw new ArgumentException($"limit must be between 1 and {MaximumUnratedLimit}.", nameof(limit));
        }

        return repository.ListProductLinesAsync(productId, count, cancellationToken);
    }

    public async Task DeleteAsync(Guid impressionId, ReceiptActor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var impression = await repository.FindForUpdateAsync(impressionId, cancellationToken);
        if (impression is null || impression.IsDeleted)
        {
            throw new ImpressionNotFoundException($"Impression '{impressionId}' was not found.");
        }

        if (!impression.IsAuthoredBy(actor.UserId))
        {
            throw new ImpressionAccessDeniedException();
        }

        // The row and its photo are kept: withdrawing hides the impression, it
        // does not destroy history. Nothing reads withdrawn rows today.
        impression.Delete(timeProvider.GetUtcNow());
        await repository.SaveChangesAsync(cancellationToken);
    }

    public async Task<(Stream Content, string ContentType)?> OpenPhotoAsync(
        Guid impressionId,
        CancellationToken cancellationToken)
    {
        var impression = await repository.FindForUpdateAsync(impressionId, cancellationToken);
        if (impression is null || impression.IsDeleted || impression.PhotoStorageKey is null)
        {
            return null;
        }

        var stream = await photos.TryOpenReadAsync(impression.PhotoStorageKey, cancellationToken);
        return stream is null ? null : (stream, impression.PhotoContentType!);
    }

    private static List<ImpressionView> WithViewer(IReadOnlyList<ImpressionView> views, string viewerUserId) =>
        views.Select(view => view with { IsMine = string.Equals(view.AuthorUserId, viewerUserId, StringComparison.Ordinal) })
            .ToList();
}
