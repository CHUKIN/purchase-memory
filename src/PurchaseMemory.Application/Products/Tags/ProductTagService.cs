using PurchaseMemory.Application.Products.Categories;
using PurchaseMemory.Application.Receipts;
using PurchaseMemory.Domain.Products;

namespace PurchaseMemory.Application.Products.Tags;

public sealed class ProductTagService : IProductTagService
{
    private readonly IProductTagRepository _repository;
    private readonly TimeProvider _timeProvider;

    public ProductTagService(IProductTagRepository repository, TimeProvider timeProvider)
    {
        _repository = repository;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<ProductTagView>> ListAsync(CancellationToken cancellationToken)
    {
        var tags = await _repository.ListTagsAsync(cancellationToken);
        var counts = await _repository.CountProductsByTagAsync(cancellationToken);
        return Order(tags).Select(tag => ToView(tag, counts)).ToList();
    }

    public async Task<ProductTagView> CreateAsync(string name, CancellationToken cancellationToken)
    {
        var (clean, key) = Clean(name);
        var tags = await _repository.ListTagsAsync(cancellationToken);
        EnsureUnique(tags, clean, key, exceptId: null);

        var tag = ProductTag.Create(clean, key, _timeProvider.GetUtcNow());
        _repository.AddTag(tag);
        await _repository.SaveChangesAsync(cancellationToken);
        return ToView(tag, new Dictionary<Guid, int>());
    }

    public async Task<ProductTagView> RenameAsync(Guid tagId, string name, CancellationToken cancellationToken)
    {
        var (clean, key) = Clean(name);
        var tags = await _repository.ListTagsAsync(cancellationToken);
        var tag = tags.FirstOrDefault(entry => entry.Id == tagId) ?? throw new ProductTagNotFoundException(tagId);
        EnsureUnique(tags, clean, key, exceptId: tagId);

        // A rename changes the label everywhere at once and is not a change of
        // any one product, so it is not written to the product audit trail.
        tag.Rename(clean, key, _timeProvider.GetUtcNow());
        await _repository.SaveChangesAsync(cancellationToken);

        var counts = await _repository.CountProductsByTagAsync(cancellationToken);
        return ToView(tag, counts);
    }

    public async Task<ProductTagDeletion> DeleteAsync(Guid tagId, ReceiptActor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var tags = await _repository.ListTagsAsync(cancellationToken);
        var tag = tags.FirstOrDefault(entry => entry.Id == tagId) ?? throw new ProductTagNotFoundException(tagId);
        var links = await _repository.ListLinksForTagAsync(tagId, cancellationToken);
        var productIds = links.Select(link => link.ProductId).ToList();
        var before = await _repository.ListTagsForProductsAsync(productIds, cancellationToken);
        var now = _timeProvider.GetUtcNow();

        foreach (var link in links)
        {
            var oldTags = before.TryGetValue(link.ProductId, out var current) ? current : [];
            _repository.AddAuditEntry(ProductAuditEntry.Create(
                link.ProductId,
                actor.UserId,
                actor.Email,
                ProductAuditEntry.TagsField,
                ProductAuditChangeSource.TagDeleted,
                Describe(oldTags),
                Describe(oldTags.Where(entry => entry.Id != tagId)),
                now));
            _repository.RemoveLink(link);
        }

        _repository.RemoveTag(tag);
        await _repository.SaveChangesAsync(cancellationToken);
        return new ProductTagDeletion(tagId, links.Count);
    }

    public async Task<IReadOnlyList<ProductTagSummary>> SetProductTagsAsync(
        Guid productId,
        IReadOnlyCollection<Guid> tagIds,
        ReceiptActor actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tagIds);
        ArgumentNullException.ThrowIfNull(actor);

        var wanted = tagIds.Distinct().ToList();
        if (wanted.Contains(Guid.Empty))
        {
            throw new ArgumentException("Tag identifiers cannot be empty.", nameof(tagIds));
        }

        if (wanted.Count > ProductTagLink.MaximumTagsPerProduct)
        {
            throw new ArgumentException(
                $"A product can carry at most {ProductTagLink.MaximumTagsPerProduct} tags.",
                nameof(tagIds));
        }

        if (!await _repository.ProductExistsAsync(productId, cancellationToken))
        {
            throw new ProductNotFoundException(productId);
        }

        var tags = (await _repository.ListTagsAsync(cancellationToken)).ToDictionary(tag => tag.Id);
        var unknown = wanted.FirstOrDefault(id => !tags.ContainsKey(id));
        if (unknown != Guid.Empty)
        {
            throw new ProductTagNotFoundException(unknown);
        }

        var links = await _repository.ListLinksForProductAsync(productId, cancellationToken);
        var oldTags = Order(links.Where(link => tags.ContainsKey(link.TagId)).Select(link => tags[link.TagId]))
            .Select(ToSummary)
            .ToList();
        var now = _timeProvider.GetUtcNow();

        foreach (var link in links.Where(link => !wanted.Contains(link.TagId)))
        {
            _repository.RemoveLink(link);
        }

        foreach (var tagId in wanted.Where(id => links.All(link => link.TagId != id)))
        {
            _repository.AddLink(ProductTagLink.Create(productId, tagId, now));
        }

        var newTags = Order(wanted.Select(id => tags[id])).Select(ToSummary).ToList();
        var oldValue = Describe(oldTags);
        var newValue = Describe(newTags);
        if (oldValue != newValue)
        {
            _repository.AddAuditEntry(ProductAuditEntry.Create(
                productId, actor.UserId, actor.Email, ProductAuditEntry.TagsField,
                ProductAuditChangeSource.Human, oldValue, newValue, now));
        }

        await _repository.SaveChangesAsync(cancellationToken);
        return newTags;
    }

    /// <summary>Audit text: tag names in display order, or the «nothing» marker.</summary>
    internal static string Describe(IEnumerable<ProductTagSummary> tags)
    {
        var names = tags.Select(tag => tag.Name).ToList();
        return names.Count == 0 ? ProductCategoryService.NoCategoryValue : string.Join(", ", names);
    }

    /// <summary>Alphabetical by the folded key, so «Акция» and «акция» never reorder between providers.</summary>
    internal static IEnumerable<ProductTag> Order(IEnumerable<ProductTag> tags) =>
        tags.OrderBy(tag => tag.NormalizedName, StringComparer.Ordinal).ThenBy(tag => tag.Name, StringComparer.Ordinal);

    private static (string Clean, string Key) Clean(string name)
    {
        var clean = ProductTag.CleanName(name);
        var key = ProductNameNormalizer.Normalize(clean);
        if (key.Length == 0)
        {
            throw new ArgumentException("Tag name needs at least one letter or digit.", nameof(name));
        }

        return (clean, key);
    }

    private static void EnsureUnique(IReadOnlyList<ProductTag> tags, string name, string key, Guid? exceptId)
    {
        if (tags.Any(tag => tag.Id != exceptId && tag.NormalizedName == key))
        {
            throw new ProductTagConflictException($"A tag named «{name}» already exists.");
        }
    }

    private static ProductTagSummary ToSummary(ProductTag tag) => new(tag.Id, tag.Name);

    private static ProductTagView ToView(ProductTag tag, IReadOnlyDictionary<Guid, int> counts) =>
        new(tag.Id, tag.Name, counts.TryGetValue(tag.Id, out var count) ? count : 0);
}
