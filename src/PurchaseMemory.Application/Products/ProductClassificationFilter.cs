using PurchaseMemory.Application.Products.Categories;
using PurchaseMemory.Application.Products.Tags;

namespace PurchaseMemory.Application.Products;

/// <summary>
/// Category, brand and tag filters after validation. <see cref="CategoryIds"/> is
/// the chosen category plus its subcategories; <c>null</c> means «any
/// category». The brand is compared in its normalized form, which folds case
/// and Polish diacritics in .NET, so SQLite and Postgres collations never
/// decide the match. <see cref="TagIds"/> are all required: a product must
/// carry every listed tag, which matches how the filters combine with each
/// other (AND).
/// </summary>
public sealed record ProductClassificationFilter(
    IReadOnlyCollection<Guid>? CategoryIds,
    bool Uncategorized,
    string? NormalizedBrand,
    IReadOnlyCollection<Guid>? TagIds = null)
{
    public bool IsEmpty => CategoryIds is null && !Uncategorized && NormalizedBrand is null && TagIds is null;

    /// <summary>
    /// Validates the raw filter shared by the catalog and the purchase feed.
    /// An unknown category is <see cref="ProductCategoryNotFoundException"/>,
    /// an unknown tag <see cref="ProductTagNotFoundException"/>;
    /// contradictory or overlong input is <see cref="ArgumentException"/>.
    /// </summary>
    public static async Task<ProductClassificationFilter> ResolveAsync(
        Guid? categoryId,
        bool uncategorized,
        string? brand,
        IReadOnlyCollection<Guid>? tagIds,
        IProductCategoryRepository categories,
        IProductTagRepository tags,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(categories);
        ArgumentNullException.ThrowIfNull(tags);

        if (uncategorized && categoryId is not null)
        {
            throw new ArgumentException("Choose either a category or «no category», not both.", nameof(categoryId));
        }

        var cleanBrand = brand?.Trim();
        if (cleanBrand?.Length > Domain.Products.Product.MaximumBrandLength)
        {
            throw new ArgumentException(
                $"Brand cannot exceed {Domain.Products.Product.MaximumBrandLength} characters.",
                nameof(brand));
        }

        IReadOnlyCollection<Guid>? categoryIds = null;
        if (categoryId is { } id)
        {
            var tree = await categories.ListCategoriesAsync(cancellationToken);
            if (tree.All(category => category.Id != id))
            {
                throw new ProductCategoryNotFoundException(id);
            }

            categoryIds = tree
                .Where(category => category.Id == id || category.ParentId == id)
                .Select(category => category.Id)
                .ToList();
        }

        var normalizedBrand = ProductNameNormalizer.Normalize(cleanBrand);
        return new ProductClassificationFilter(
            categoryIds,
            uncategorized,
            normalizedBrand.Length > 0 ? normalizedBrand : null,
            await ResolveTagsAsync(tagIds, tags, cancellationToken));
    }

    private static async Task<IReadOnlyCollection<Guid>?> ResolveTagsAsync(
        IReadOnlyCollection<Guid>? tagIds,
        IProductTagRepository tags,
        CancellationToken cancellationToken)
    {
        if (tagIds is null || tagIds.Count == 0)
        {
            return null;
        }

        var wanted = tagIds.Distinct().ToList();
        if (wanted.Count > Domain.Products.ProductTagLink.MaximumTagsPerProduct)
        {
            throw new ArgumentException(
                $"Filter by at most {Domain.Products.ProductTagLink.MaximumTagsPerProduct} tags.",
                nameof(tagIds));
        }

        var known = (await tags.ListTagsAsync(cancellationToken)).Select(tag => tag.Id).ToHashSet();
        var unknown = wanted.FirstOrDefault(id => !known.Contains(id));
        if (wanted.Contains(Guid.Empty) || unknown != Guid.Empty)
        {
            throw new ProductTagNotFoundException(unknown);
        }

        return wanted;
    }
}
