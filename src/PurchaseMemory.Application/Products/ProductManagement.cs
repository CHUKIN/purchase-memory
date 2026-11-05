using Microsoft.Extensions.Logging;
using PurchaseMemory.Application.Products.Categories;
using PurchaseMemory.Application.Products.Tags;
using PurchaseMemory.Application.Receipts;
using PurchaseMemory.Domain.Products;

namespace PurchaseMemory.Application.Products;

/// <summary>
/// Changes a person makes to the catalog itself: creating a product by hand
/// (or from a scanned barcode), renaming it, merging two products into one
/// and settling a «проверить» flag. Every change leaves a product audit entry.
/// </summary>
public interface IProductManagementService
{
    /// <exception cref="ProductConflictException">A product with the same normalized name exists.</exception>
    /// <exception cref="ProductCategoryNotFoundException">The chosen category does not exist.</exception>
    Task<Product> CreateAsync(CreateProductCommand command, ReceiptActor actor, CancellationToken cancellationToken);

    /// <exception cref="ProductNotFoundException">No such product.</exception>
    Task<Product> RenameAsync(Guid productId, string name, ReceiptActor actor, CancellationToken cancellationToken);

    /// <summary>
    /// Moves everything of <paramref name="sourceId"/> to <paramref name="targetId"/>
    /// and deletes the source. There is no undo; the audit entry on the target
    /// names both products so it can be redone by hand.
    /// </summary>
    /// <exception cref="ProductNotFoundException">Either product does not exist (also: already merged away).</exception>
    /// <exception cref="ArgumentException">Source and target are the same product.</exception>
    /// <exception cref="ProductConflictException">Someone changed the same products at the same time.</exception>
    Task<ProductMergeResult> MergeAsync(Guid sourceId, Guid targetId, ReceiptActor actor, CancellationToken cancellationToken);

    /// <summary>
    /// «Это другой товар»: the flagged product stays as it is, its flag and
    /// candidate list go away and its receipt wording becomes a confirmed alias.
    /// Calling it on an unflagged product changes nothing.
    /// </summary>
    /// <exception cref="ProductNotFoundException">No such product.</exception>
    Task<Product> ConfirmDistinctAsync(Guid productId, ReceiptActor actor, CancellationToken cancellationToken);
}

public sealed record CreateProductCommand(
    string? Name,
    string? Brand = null,
    Guid? CategoryId = null,
    ProductOrigin Origin = ProductOrigin.Manual);

public sealed record ProductMergeResult(
    Guid SourceProductId,
    string SourceName,
    Guid TargetProductId,
    string TargetName,
    int MovedLineCount,
    int MovedAliasCount);

/// <summary>The request conflicts with the catalog as it is (duplicate, concurrent change).</summary>
public sealed class ProductConflictException(string message, Guid? existingProductId = null, Exception? innerException = null)
    : Exception(message, innerException)
{
    /// <summary>For a duplicate on create: the product that already carries the name.</summary>
    public Guid? ExistingProductId { get; } = existingProductId;
}

/// <summary>What a merge moved, as counted by the storage layer.</summary>
public sealed record ProductReferenceMove(int Lines, int Aliases, int Impressions, int AuditEntries, int Photos = 0, int Barcodes = 0);

/// <summary>Persistence for <see cref="IProductManagementService"/>.</summary>
public interface IProductMaintenanceRepository
{
    /// <summary>Tracked, so the caller may change it.</summary>
    Task<Product?> FindProductForUpdateAsync(Guid productId, CancellationToken cancellationToken);

    Task<Product?> FindByNormalizedNameAsync(string normalizedName, CancellationToken cancellationToken);

    void AddProduct(Product product);

    void RemoveProduct(Product product);

    void AddAuditEntry(ProductAuditEntry entry);

    /// <summary>Tracked aliases of one product.</summary>
    Task<IReadOnlyList<ProductAlias>> ListAliasesForUpdateAsync(Guid productId, CancellationToken cancellationToken);

    /// <summary>Drops the candidate list of a flagged product.</summary>
    Task RemoveReviewCandidatesAsync(Guid productId, CancellationToken cancellationToken);

    /// <summary>
    /// Repoints every row that refers to <paramref name="sourceId"/> at
    /// <paramref name="targetId"/>: purchase lines, impressions, aliases (which
    /// become human-confirmed: a person just said this wording is the target),
    /// audit entries, photos and barcodes (the target's main photo stays main),
    /// and other flagged products' candidates (a candidate that
    /// would point at its own product, or repeat one already listed, is
    /// dropped). The source's own tag links and candidate list are deleted;
    /// tags are re-linked by the caller. Runs immediately, so call it inside
    /// <see cref="InTransactionAsync"/>.
    /// </summary>
    Task<ProductReferenceMove> MoveReferencesAsync(Guid sourceId, Guid targetId, CancellationToken cancellationToken);

    /// <summary>Runs <paramref name="work"/> in one database transaction.</summary>
    Task InTransactionAsync(Func<Task> work, CancellationToken cancellationToken);

    /// <summary>
    /// Saves; a row that changed or vanished in between surfaces as
    /// <see cref="ProductConflictException"/>.
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed partial class ProductManagementService(
    IProductMaintenanceRepository repository,
    IProductTagRepository tags,
    IProductCategoryRepository categories,
    IProductCategorizationDispatcher categorization,
    TimeProvider timeProvider,
    ILogger<ProductManagementService> logger) : IProductManagementService
{
    /// <summary>Audit value for «a person said this is a product of its own».</summary>
    public const string DistinctValue = "Distinct";

    public async Task<Product> CreateAsync(CreateProductCommand command, ReceiptActor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(actor);
        if (command.Origin == ProductOrigin.Receipt || !Enum.IsDefined(command.Origin))
        {
            throw new ArgumentException("A product created here comes from a person or a barcode.", nameof(command));
        }

        var (name, normalized) = CleanName(command.Name);
        if (command.Brand?.Trim().Length > Product.MaximumBrandLength)
        {
            throw new ArgumentException($"Brand cannot exceed {Product.MaximumBrandLength} characters.", nameof(command));
        }

        // An exact namesake is almost always the same thing typed twice (or the
        // same barcode scanned on two phones); the caller can open that one.
        if (await repository.FindByNormalizedNameAsync(normalized, cancellationToken) is { } existing)
        {
            throw new ProductConflictException($"A product named «{existing.CanonicalName}» already exists.", existing.Id);
        }

        string? categoryName = null;
        if (command.CategoryId is { } categoryId)
        {
            categoryName = (await categories.ListCategoriesAsync(cancellationToken))
                .FirstOrDefault(category => category.Id == categoryId)?.Name
                ?? throw new ProductCategoryNotFoundException(categoryId);
        }

        var now = timeProvider.GetUtcNow();
        var product = Product.Create(name, normalized, now, command.Origin);
        if (command.CategoryId is not null)
        {
            product.ConfirmCategory(command.CategoryId, now);
        }

        if (Product.CleanBrand(command.Brand) is { } brand)
        {
            product.ConfirmBrand(brand, ProductNameNormalizer.Normalize(brand), now);
        }

        repository.AddProduct(product);
        Audit(product.Id, actor, ProductAuditEntry.CreatedField, null, OriginText(command.Origin), now);
        if (categoryName is not null)
        {
            Audit(product.Id, actor, ProductAuditEntry.CategoryField, null, categoryName, now);
        }

        if (product.Brand is not null)
        {
            Audit(product.Id, actor, ProductAuditEntry.BrandField, null, product.Brand, now);
        }

        await repository.SaveChangesAsync(cancellationToken);
        ProductCreated(product.Id, command.Origin);

        // Like a product born from a receipt, it gets a suggested category and
        // human names in the background (the person never typed those here).
        if (product.CategorySource is null || product.NamePlSource is null || product.NameRuSource is null)
        {
            categorization.Enqueue([product.Id]);
        }

        return product;
    }

    public async Task<Product> RenameAsync(Guid productId, string name, ReceiptActor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var (clean, normalized) = CleanName(name);
        var product = await repository.FindProductForUpdateAsync(productId, cancellationToken)
            ?? throw new ProductNotFoundException(productId);
        if (product.CanonicalName == clean)
        {
            return product;
        }

        var now = timeProvider.GetUtcNow();
        var oldName = product.CanonicalName;
        product.Rename(clean, normalized, now);
        Audit(product.Id, actor, ProductAuditEntry.NameField, oldName, product.CanonicalName, now);
        await repository.SaveChangesAsync(cancellationToken);
        return product;
    }

    public async Task<ProductMergeResult> MergeAsync(
        Guid sourceId,
        Guid targetId,
        ReceiptActor actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (sourceId == targetId)
        {
            throw new ArgumentException("A product cannot be merged into itself.", nameof(targetId));
        }

        ProductMergeResult? result = null;
        await repository.InTransactionAsync(async () =>
        {
            var source = await repository.FindProductForUpdateAsync(sourceId, cancellationToken)
                ?? throw new ProductNotFoundException(sourceId);
            var target = await repository.FindProductForUpdateAsync(targetId, cancellationToken)
                ?? throw new ProductNotFoundException(targetId);
            var now = timeProvider.GetUtcNow();

            // Read before the move deletes the source's links.
            var tagsByProduct = await tags.ListTagsForProductsAsync([source.Id, target.Id], cancellationToken);
            var sourceTags = tagsByProduct.GetValueOrDefault(source.Id) ?? [];
            var targetTags = tagsByProduct.GetValueOrDefault(target.Id) ?? [];

            var moved = await repository.MoveReferencesAsync(source.Id, target.Id, cancellationToken);

            // Union of tags. The per-product limit still holds: the target's own
            // tags stay, the source's fill the remaining room alphabetically.
            var room = ProductTagLink.MaximumTagsPerProduct - targetTags.Count;
            var added = sourceTags
                .Where(tag => targetTags.All(existing => existing.Id != tag.Id))
                .Take(Math.Max(0, room))
                .ToList();
            foreach (var tag in added)
            {
                tags.AddLink(ProductTagLink.Create(target.Id, tag.Id, now));
            }

            if (added.Count > 0)
            {
                Audit(
                    target.Id,
                    actor,
                    ProductAuditEntry.TagsField,
                    ProductTagService.Describe(targetTags),
                    ProductTagService.Describe(targetTags.Concat(added).OrderBy(tag => tag.Name, StringComparer.Ordinal)),
                    now);
            }

            var categoryNames = (await categories.ListCategoriesAsync(cancellationToken))
                .ToDictionary(category => category.Id, category => category.Name);
            var oldCategory = ProductCategoryService.Describe(target.CategoryId, target.CategorySource, categoryNames);
            var oldBrand = ProductCategoryService.DescribeBrand(target.Brand, target.BrandSource);
            var (categoryChanged, brandChanged) = target.AbsorbClassificationFrom(source, now);
            if (categoryChanged)
            {
                Audit(target.Id, actor, ProductAuditEntry.CategoryField, oldCategory,
                    ProductCategoryService.Describe(target.CategoryId, target.CategorySource, categoryNames), now);
            }

            if (brandChanged)
            {
                Audit(target.Id, actor, ProductAuditEntry.BrandField, oldBrand,
                    ProductCategoryService.DescribeBrand(target.Brand, target.BrandSource), now);
            }

            var oldPolish = ProductCategoryService.DescribeName(target.NamePl, target.NamePlSource);
            var oldRussian = ProductCategoryService.DescribeName(target.NameRu, target.NameRuSource);
            var (polishChanged, russianChanged) = target.AbsorbNamesFrom(source, now);
            if (polishChanged)
            {
                Audit(target.Id, actor, ProductAuditEntry.NamePlField, oldPolish,
                    ProductCategoryService.DescribeName(target.NamePl, target.NamePlSource), now);
            }

            if (russianChanged)
            {
                Audit(target.Id, actor, ProductAuditEntry.NameRuField, oldRussian,
                    ProductCategoryService.DescribeName(target.NameRu, target.NameRuSource), now);
            }

            if (target.AbsorbAvoidFrom(source, now))
            {
                Audit(target.Id, actor, ProductAuditEntry.AvoidField, null, Media.ProductMediaService.AvoidValue, now);
            }

            Audit(target.Id, actor, ProductAuditEntry.MergeField, Describe(source), Describe(target), now);
            repository.RemoveProduct(source);
            await repository.SaveChangesAsync(cancellationToken);

            result = new ProductMergeResult(
                source.Id,
                source.CanonicalName,
                target.Id,
                target.CanonicalName,
                moved.Lines,
                moved.Aliases);
            ProductsMerged(source.Id, target.Id, moved.Lines, moved.Aliases, moved.Impressions, added.Count, moved.Photos, moved.Barcodes);
        }, cancellationToken);

        return result!;
    }

    public async Task<Product> ConfirmDistinctAsync(Guid productId, ReceiptActor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        Product? confirmed = null;
        await repository.InTransactionAsync(async () =>
        {
            var product = await repository.FindProductForUpdateAsync(productId, cancellationToken)
                ?? throw new ProductNotFoundException(productId);
            var now = timeProvider.GetUtcNow();
            var reason = product.ReviewReason;
            if (product.ClearReviewFlag(now))
            {
                foreach (var alias in await repository.ListAliasesForUpdateAsync(product.Id, cancellationToken))
                {
                    alias.ConfirmFor(product.Id, alias.RawText);
                }

                await repository.RemoveReviewCandidatesAsync(product.Id, cancellationToken);
                Audit(product.Id, actor, ProductAuditEntry.ReviewField, reason?.ToString(), DistinctValue, now);
                await repository.SaveChangesAsync(cancellationToken);
            }

            confirmed = product;
        }, cancellationToken);

        return confirmed!;
    }

    private void Audit(Guid productId, ReceiptActor actor, string field, string? oldValue, string? newValue, DateTimeOffset now)
    {
        repository.AddAuditEntry(ProductAuditEntry.Create(
            productId, actor.UserId, actor.Email, field, ProductAuditChangeSource.Human, oldValue, newValue, now));
    }

    private static string Describe(Product product) => $"{product.CanonicalName} ({product.Id:D})";

    private static string OriginText(ProductOrigin origin) => origin == ProductOrigin.Barcode ? "barcode" : "manual";

    /// <summary>A person's name is refused when too long rather than cut, unlike receipt text.</summary>
    private static (string Name, string Normalized) CleanName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Product name is required.", nameof(name));
        }

        var clean = string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (clean.Length > Product.MaximumNameLength)
        {
            throw new ArgumentException($"Product name cannot exceed {Product.MaximumNameLength} characters.", nameof(name));
        }

        var normalized = ProductNameNormalizer.Normalize(clean);
        if (normalized.Length == 0)
        {
            throw new ArgumentException("Product name needs at least one letter or digit.", nameof(name));
        }

        return (clean, normalized.Length <= Product.MaximumNameLength ? normalized : normalized[..Product.MaximumNameLength]);
    }

    [LoggerMessage(EventId = 3401, Level = LogLevel.Information,
        Message = "Product {ProductId} created by hand ({Origin})")]
    private partial void ProductCreated(Guid productId, ProductOrigin origin);

    [LoggerMessage(EventId = 3402, Level = LogLevel.Information,
        Message = "Product {SourceProductId} merged into {TargetProductId}: {Lines} lines, {Aliases} aliases, {Impressions} impressions, {AddedTags} tags, {Photos} photos, {Barcodes} barcodes moved")]
    private partial void ProductsMerged(Guid sourceProductId, Guid targetProductId, int lines, int aliases, int impressions, int addedTags, int photos, int barcodes);
}
