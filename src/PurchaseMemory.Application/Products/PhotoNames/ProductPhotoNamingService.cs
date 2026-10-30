using Microsoft.Extensions.Logging;
using PurchaseMemory.Application.Products.Categories;
using PurchaseMemory.Application.Receipts;
using PurchaseMemory.Domain.Products;

namespace PurchaseMemory.Application.Products.PhotoNames;

public interface IProductPhotoNamingService
{
    /// <summary>
    /// Reads the names from a package photo and applies them per language: a
    /// missing or suggested name is replaced (still «suggested»), a person's
    /// name stays and the reading comes back as a proposal.
    /// </summary>
    Task<ProductPhotoNamingResult> NameFromPhotoAsync(
        Guid productId,
        ProductPhotoImage image,
        ReceiptActor actor,
        CancellationToken cancellationToken);

    /// <summary>
    /// «Вернуть»: puts back the name a photo replaced, if the photo's name is
    /// still there unchanged; otherwise a <see cref="ProductCategoryConflictException"/>.
    /// </summary>
    Task<ProductNamesView> UndoAsync(
        Guid productId,
        ProductNameLanguage language,
        string photoName,
        string? previousName,
        ReceiptActor actor,
        CancellationToken cancellationToken);
}

public enum PhotoNameOutcome
{
    /// <summary>The model gave no name for this language (not sure).</summary>
    NotRead,

    /// <summary>The name was missing or suggested and is now the photo's.</summary>
    Replaced,

    /// <summary>A person's name stays; the photo's name is only offered.</summary>
    Proposed,

    /// <summary>The photo says what the product is already called.</summary>
    Unchanged
}

/// <param name="Name">The name read from the photo (null when <see cref="PhotoNameOutcome.NotRead"/>).</param>
/// <param name="PreviousName">Before a replacement: what «Вернуть» puts back (null = the receipt text showed).</param>
public sealed record PhotoNameLanguageResult(
    ProductNameLanguage Language,
    PhotoNameOutcome Outcome,
    string? Name,
    string? PreviousName,
    ProductAttributeSource? PreviousSource);

public sealed record ProductPhotoNamingResult(
    ProductNamesView Names,
    IReadOnlyList<PhotoNameLanguageResult> Languages);

public sealed partial class ProductPhotoNamingService : IProductPhotoNamingService
{
    private readonly IProductCategoryRepository _repository;
    private readonly IProductPhotoNamer _namer;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ProductPhotoNamingService> _logger;

    public ProductPhotoNamingService(
        IProductCategoryRepository repository,
        IProductPhotoNamer namer,
        TimeProvider timeProvider,
        ILogger<ProductPhotoNamingService> logger)
    {
        _repository = repository;
        _namer = namer;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<ProductPhotoNamingResult> NameFromPhotoAsync(
        Guid productId,
        ProductPhotoImage image,
        ReceiptActor actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(actor);
        var before = await _repository.FindProductSnapshotAsync(productId, cancellationToken)
            ?? throw new ProductNotFoundException(productId);
        if (!_namer.IsConfigured)
        {
            NamerNotConfigured(productId);
            throw new ProductPhotoNamingUnavailableException("Reading names from photos is not configured.");
        }

        // Even with both names confirmed the call is made: the person asked,
        // and the answer comes back as a proposal they may take.
        var reading = await _namer.ReadAsync(image, new ProductPhotoHint(before.CanonicalName, before.Brand), cancellationToken);
        var names = ProductNameSuggestion.From(reading.NamePl, reading.NameRu);
        var now = _timeProvider.GetUtcNow();
        var written = await _repository.ApplyNameSuggestionAsync(productId, names, replaceSuggested: true, now, cancellationToken);

        if (written.PolishWritten)
        {
            Audit(productId, actor, ProductNameLanguage.Polish, before.NamePl, before.NamePlSource, names.NamePl, now);
        }

        if (written.RussianWritten)
        {
            Audit(productId, actor, ProductNameLanguage.Russian, before.NameRu, before.NameRuSource, names.NameRu, now);
        }

        if (written.PolishWritten || written.RussianWritten)
        {
            await _repository.SaveChangesAsync(cancellationToken);
        }

        // Re-read: the conditional writes bypass the change tracker, and a
        // person may have confirmed a name while the model was reading.
        var after = await _repository.FindProductSnapshotAsync(productId, cancellationToken)
            ?? throw new ProductNotFoundException(productId);
        var languages = new[]
        {
            Outcome(ProductNameLanguage.Polish, names.NamePl, written.PolishWritten, before, after),
            Outcome(ProductNameLanguage.Russian, names.NameRu, written.RussianWritten, before, after)
        };
        PhotoNamed(productId, languages[0].Outcome, languages[1].Outcome, reading.InputTokens, reading.OutputTokens, reading.CostUsd);
        return new ProductPhotoNamingResult(ProductNamesView.From(after), languages);
    }

    public async Task<ProductNamesView> UndoAsync(
        Guid productId,
        ProductNameLanguage language,
        string photoName,
        string? previousName,
        ReceiptActor actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!Enum.IsDefined(language))
        {
            throw new ArgumentException("Unknown name language.", nameof(language));
        }

        var expected = Product.CleanDisplayName(photoName)
            ?? throw new ArgumentException("photoName is required.", nameof(photoName));
        var product = await _repository.FindProductForUpdateAsync(productId, cancellationToken)
            ?? throw new ProductNotFoundException(productId);
        var (current, source) = product.NameIn(language);
        if (current != expected || source != ProductAttributeSource.Suggested)
        {
            // Someone edited or confirmed the name since: their decision wins.
            throw new ProductCategoryConflictException("The name changed since the photo set it. Nothing was undone.");
        }

        var restored = Product.CleanDisplayName(previousName);
        var now = _timeProvider.GetUtcNow();
        product.RestoreSuggestedName(language, restored, ProductNameNormalizer.Normalize(restored), now);
        var (newName, newSource) = product.NameIn(language);
        _repository.AddAuditEntry(ProductAuditEntry.Create(
            product.Id, actor.UserId, actor.Email, NameField(language), ProductAuditChangeSource.Human,
            ProductCategoryService.DescribeName(current, source),
            ProductCategoryService.DescribeName(newName, newSource),
            now));
        await _repository.SaveChangesAsync(cancellationToken);
        return ProductNamesView.From(product);
    }

    private static PhotoNameLanguageResult Outcome(
        ProductNameLanguage language,
        string? read,
        bool written,
        Product before,
        Product after)
    {
        var (previousName, previousSource) = before.NameIn(language);
        if (read is null)
        {
            return new PhotoNameLanguageResult(language, PhotoNameOutcome.NotRead, null, null, null);
        }

        if (written)
        {
            return new PhotoNameLanguageResult(language, PhotoNameOutcome.Replaced, read, previousName, previousSource);
        }

        var (currentName, currentSource) = after.NameIn(language);
        return currentSource == ProductAttributeSource.Confirmed && currentName != read
            ? new PhotoNameLanguageResult(language, PhotoNameOutcome.Proposed, read, currentName, currentSource)
            : new PhotoNameLanguageResult(language, PhotoNameOutcome.Unchanged, read, null, null);
    }

    /// <summary>
    /// The person who sent the photo is the actor; the source says a model
    /// wrote the value, like every other suggestion in the trail.
    /// </summary>
    private void Audit(
        Guid productId,
        ReceiptActor actor,
        ProductNameLanguage language,
        string? oldName,
        ProductAttributeSource? oldSource,
        string? newName,
        DateTimeOffset now)
    {
        _repository.AddAuditEntry(ProductAuditEntry.Create(
            productId, actor.UserId, actor.Email, NameField(language), ProductAuditChangeSource.Suggestion,
            ProductCategoryService.DescribeName(oldName, oldSource),
            ProductCategoryService.DescribeName(newName, ProductAttributeSource.Suggested),
            now));
    }

    private static string NameField(ProductNameLanguage language) =>
        language == ProductNameLanguage.Polish ? ProductAuditEntry.NamePlField : ProductAuditEntry.NameRuField;

    [LoggerMessage(EventId = 3431, Level = LogLevel.Warning,
        Message = "Name from photo for product {ProductId} is unavailable: the namer is switched off or has no model/credential configured")]
    private partial void NamerNotConfigured(Guid productId);

    [LoggerMessage(EventId = 3432, Level = LogLevel.Information,
        Message = "Name from photo for product {ProductId}: Polish {PolishOutcome}, Russian {RussianOutcome}; input tokens {InputTokens}; output tokens {OutputTokens}; estimated cost USD {CostUsd}")]
    private partial void PhotoNamed(
        Guid productId,
        PhotoNameOutcome polishOutcome,
        PhotoNameOutcome russianOutcome,
        long? inputTokens,
        long? outputTokens,
        decimal? costUsd);
}
