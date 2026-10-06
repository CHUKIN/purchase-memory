using PurchaseMemory.Application.Receipts;
using PurchaseMemory.Domain.Products;

namespace PurchaseMemory.Application.Products.Categories;

public sealed class ProductCategoryService : IProductCategoryService
{
    /// <summary>Marker the audit trail uses for «no category».</summary>
    public const string NoCategoryValue = "—";


    private readonly IProductCategoryRepository _repository;
    private readonly TimeProvider _timeProvider;

    public ProductCategoryService(IProductCategoryRepository repository, TimeProvider timeProvider)
    {
        _repository = repository;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<ProductCategoryView>> GetTreeAsync(CancellationToken cancellationToken)
    {
        var categories = await _repository.ListCategoriesAsync(cancellationToken);
        var counts = await _repository.CountProductsByCategoryAsync(cancellationToken);
        return Order(categories).Select(category => ToView(category, counts)).ToList();
    }

    public async Task<ProductCategoryView> CreateAsync(string name, Guid? parentId, CancellationToken cancellationToken)
    {
        var normalized = NormalizeName(name);
        var categories = await _repository.ListCategoriesAsync(cancellationToken);

        if (parentId is not null)
        {
            var parent = categories.FirstOrDefault(category => category.Id == parentId)
                ?? throw new ProductCategoryNotFoundException(parentId.Value);
            if (parent.ParentId is not null)
            {
                throw new ProductCategoryConflictException("Categories are two levels deep: a subcategory cannot have children.");
            }
        }

        EnsureUniqueAmongSiblings(categories, parentId, normalized, exceptId: null);

        var position = categories.Where(category => category.ParentId == parentId)
            .Select(category => category.Position + 1)
            .DefaultIfEmpty(0)
            .Max();
        var created = ProductCategory.Create(parentId, normalized, position, _timeProvider.GetUtcNow());
        _repository.AddCategory(created);
        await _repository.SaveChangesAsync(cancellationToken);
        return ToView(created, new Dictionary<Guid, int>());
    }

    public async Task<ProductCategoryView> RenameAsync(Guid categoryId, string name, CancellationToken cancellationToken)
    {
        var normalized = NormalizeName(name);
        var categories = await _repository.ListCategoriesAsync(cancellationToken);
        var category = categories.FirstOrDefault(entry => entry.Id == categoryId)
            ?? throw new ProductCategoryNotFoundException(categoryId);

        EnsureUniqueAmongSiblings(categories, category.ParentId, normalized, exceptId: categoryId);
        category.Rename(normalized, _timeProvider.GetUtcNow());
        await _repository.SaveChangesAsync(cancellationToken);

        var counts = await _repository.CountProductsByCategoryAsync(cancellationToken);
        return ToView(category, counts);
    }

    public async Task<ProductCategoryDeletion> DeleteAsync(
        Guid categoryId,
        ReceiptActor actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var categories = await _repository.ListCategoriesAsync(cancellationToken);
        var category = categories.FirstOrDefault(entry => entry.Id == categoryId)
            ?? throw new ProductCategoryNotFoundException(categoryId);

        if (categories.Any(entry => entry.ParentId == categoryId))
        {
            throw new ProductCategoryConflictException(
                "Delete or move the subcategories first: this category still has some.");
        }

        var parent = categories.FirstOrDefault(entry => entry.Id == category.ParentId);
        var now = _timeProvider.GetUtcNow();
        var products = await _repository.ListProductsInCategoryAsync(categoryId, cancellationToken);
        foreach (var product in products)
        {
            product.MoveOutOfDeletedCategory(parent?.Id, now);
            _repository.AddAuditEntry(ProductAuditEntry.Create(
                product.Id,
                actor.UserId,
                actor.Email,
                ProductAuditEntry.CategoryField,
                ProductAuditChangeSource.CategoryDeleted,
                category.Name,
                parent?.Name ?? NoCategoryValue,
                now));
        }

        _repository.RemoveCategory(category);
        await _repository.SaveChangesAsync(cancellationToken);
        return new ProductCategoryDeletion(categoryId, parent?.Id, products.Count);
    }

    public async Task<ProductClassificationView> ClassifyProductAsync(
        ClassifyProductCommand command,
        ReceiptActor actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(actor);

        if (!command.SetCategory && !command.SetBrand)
        {
            throw new ArgumentException("Nothing to change: set a category, a brand or both.", nameof(command));
        }

        if (command.Brand?.Trim().Length > Product.MaximumBrandLength)
        {
            throw new ArgumentException($"Brand cannot exceed {Product.MaximumBrandLength} characters.", nameof(command));
        }

        var product = await _repository.FindProductForUpdateAsync(command.ProductId, cancellationToken)
            ?? throw new ProductNotFoundException(command.ProductId);
        var categories = await _repository.ListCategoriesAsync(cancellationToken);
        var names = categories.ToDictionary(category => category.Id, category => category.Name);
        var now = _timeProvider.GetUtcNow();

        if (command.SetCategory)
        {
            if (command.CategoryId is { } categoryId && !names.ContainsKey(categoryId))
            {
                throw new ProductCategoryNotFoundException(categoryId);
            }

            var oldValue = Describe(product.CategoryId, product.CategorySource, names);
            product.ConfirmCategory(command.CategoryId, now);
            var newValue = Describe(product.CategoryId, product.CategorySource, names);
            if (oldValue != newValue)
            {
                _repository.AddAuditEntry(ProductAuditEntry.Create(
                    product.Id, actor.UserId, actor.Email, ProductAuditEntry.CategoryField,
                    ProductAuditChangeSource.Human, oldValue, newValue, now));
            }
        }

        if (command.SetBrand)
        {
            var oldValue = DescribeBrand(product.Brand, product.BrandSource);
            product.ConfirmBrand(command.Brand, ProductNameNormalizer.Normalize(command.Brand), now);
            var newValue = DescribeBrand(product.Brand, product.BrandSource);
            if (oldValue != newValue)
            {
                _repository.AddAuditEntry(ProductAuditEntry.Create(
                    product.Id, actor.UserId, actor.Email, ProductAuditEntry.BrandField,
                    ProductAuditChangeSource.Human, oldValue, newValue, now));
            }
        }

        await _repository.SaveChangesAsync(cancellationToken);
        return ToClassification(product);
    }

    public async Task<IReadOnlyList<ProductAuditEntryView>> GetAuditTrailAsync(
        Guid productId,
        CancellationToken cancellationToken)
    {
        var entries = await _repository.ListAuditEntriesAsync(productId, cancellationToken);
        return entries
            .OrderByDescending(entry => entry.OccurredAt)
            .Select(entry => new ProductAuditEntryView(
                entry.Actor,
                entry.ActorEmail,
                entry.FieldName,
                entry.Source,
                entry.OldValue,
                entry.NewValue,
                entry.OccurredAt))
            .ToList();
    }

    public static ProductClassificationView ToClassification(Product product) =>
        new(product.Id, product.CategoryId, product.CategorySource, product.Brand, product.BrandSource);

    /// <summary>Audit text for a category; a suggestion is marked so confirming it leaves a trace.</summary>
    internal static string Describe(Guid? categoryId, ProductAttributeSource? source, IReadOnlyDictionary<Guid, string> names)
    {
        var name = categoryId is { } id && names.TryGetValue(id, out var found) ? found : NoCategoryValue;
        return source == ProductAttributeSource.Suggested ? $"{name} (suggested)" : name;
    }

    internal static string DescribeBrand(string? brand, ProductAttributeSource? source)
    {
        var name = brand ?? NoCategoryValue;
        return source == ProductAttributeSource.Suggested ? $"{name} (suggested)" : name;
    }


    private static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Category name is required.", nameof(name));
        }

        return ProductCategory.NormalizeName(name);
    }

    private static void EnsureUniqueAmongSiblings(
        IReadOnlyList<ProductCategory> categories,
        Guid? parentId,
        string name,
        Guid? exceptId)
    {
        var key = ProductNameNormalizer.Normalize(name);
        if (categories.Any(category =>
                category.ParentId == parentId
                && category.Id != exceptId
                && ProductNameNormalizer.Normalize(category.Name) == key))
        {
            throw new ProductCategoryConflictException($"A category named «{name}» already exists here.");
        }
    }

    /// <summary>Parents first, each followed by its children, siblings by position.</summary>
    private static IEnumerable<ProductCategory> Order(IReadOnlyList<ProductCategory> categories)
    {
        foreach (var parent in categories.Where(category => category.ParentId is null)
                     .OrderBy(category => category.Position).ThenBy(category => category.Name, StringComparer.Ordinal))
        {
            yield return parent;
            foreach (var child in categories.Where(category => category.ParentId == parent.Id)
                         .OrderBy(category => category.Position).ThenBy(category => category.Name, StringComparer.Ordinal))
            {
                yield return child;
            }
        }
    }

    private static ProductCategoryView ToView(ProductCategory category, IReadOnlyDictionary<Guid, int> counts) =>
        new(
            category.Id,
            category.ParentId,
            category.Name,
            category.SystemKey,
            category.Position,
            counts.TryGetValue(category.Id, out var count) ? count : 0);
}
