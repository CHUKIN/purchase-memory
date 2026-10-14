using PurchaseMemory.Application.Products;
using PurchaseMemory.Application.Products.Categories;
using PurchaseMemory.Application.Receipts;
using PurchaseMemory.Domain.Products;
using PurchaseMemory.Domain.Purchases;
using PurchaseMemory.Domain.Receipts;

namespace PurchaseMemory.Application.Purchases;

/// <summary>What a person decided about a line's catalog product.</summary>
public enum ProductLinkChoice
{
    /// <summary>No decision: the deterministic matcher links the line (or asks again).</summary>
    Automatic,

    /// <summary>Link to the product given in <see cref="EditPurchaseItemCommand.ProductId"/>.</summary>
    Existing,

    /// <summary>Create a new catalog product named after the line and link to it.</summary>
    CreateNew
}

/// <param name="BoundingBox">Echoed back from the detail view so the receipt photo region survives an edit.</param>
/// <param name="LineId">
/// The saved line this one continues (its id from the detail view); null for a
/// new line. Used only to keep the line's «checked» mark across the edit.
/// </param>
public sealed record EditPurchaseItemCommand(
    string Name,
    decimal? Quantity,
    string? Unit,
    decimal? UnitPrice,
    decimal? LineTotal,
    ReceiptLineKind? Kind = null,
    ProductLinkChoice Link = ProductLinkChoice.Automatic,
    Guid? ProductId = null,
    BoundingBox? BoundingBox = null,
    Guid? LineId = null);

/// <param name="ExpectedUpdatedAt">
/// The <c>UpdatedAt</c> the editor started from. A different stored value means
/// someone else saved in between, and the edit is refused instead of silently
/// overwriting their change.
/// </param>
public sealed record EditPurchaseCommand(
    Guid PurchaseId,
    DateTimeOffset? ExpectedUpdatedAt,
    string? MerchantName,
    DateTimeOffset? PurchasedAt,
    string? Currency,
    decimal? ReceiptTotal,
    IReadOnlyList<EditPurchaseItemCommand> Items,
    ReceiptActor? Actor = null);

public sealed class PurchaseConcurrencyException(Guid purchaseId)
    : Exception($"Purchase '{purchaseId}' was changed after it was opened. Reload it and apply the correction again.")
{
    public Guid PurchaseId { get; } = purchaseId;
}

public sealed record PurchaseProductCandidateView(Guid ProductId, string Name, double Score);

public sealed record PurchaseDetailItemView(
    int Position,
    string Name,
    decimal? Quantity,
    string? Unit,
    decimal? UnitPrice,
    decimal? LineTotal,
    ReceiptLineKind Kind,
    int? DiscountedItemPosition,
    Guid? ProductId,
    string? ProductName,
    bool PendingMatch,
    IReadOnlyList<PurchaseProductCandidateView> Candidates,
    BoundingBox? BoundingBox,
    Guid Id,
    DateTimeOffset? ApprovedAt = null,
    string? ApprovedByEmail = null);

public sealed record PurchaseDetailView(
    Guid PurchaseId,
    Guid? SourceReceiptId,
    string? MerchantName,
    DateTimeOffset? PurchasedAt,
    string? Currency,
    decimal? ReceiptTotal,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<PurchaseDetailItemView> Items,
    IReadOnlyList<PurchaseDraftIssue> Issues);

/// <summary>
/// Viewing and correcting an already saved purchase from the app. It reuses the
/// same item factory, domain validation, issue detector and matcher as saving,
/// so a corrected purchase is indistinguishable from one saved right the first time.
/// </summary>
public interface IPurchaseEditService
{
    Task<PurchaseDetailView> GetAsync(Guid purchaseId, CancellationToken cancellationToken);

    Task<PurchaseDetailView> UpdateAsync(EditPurchaseCommand command, CancellationToken cancellationToken);
}

public sealed class PurchaseEditService(
    IPurchaseDraftRepository purchases,
    IProductCatalogRepository catalog,
    IReceiptCaptureRepository receipts,
    IProductMatcher matcher,
    IProductCategorizationDispatcher categorization,
    TimeProvider timeProvider) : IPurchaseEditService
{
    public const int MaximumItems = 500;

    public async Task<PurchaseDetailView> GetAsync(Guid purchaseId, CancellationToken cancellationToken)
    {
        var draft = await FindAsync(purchaseId, cancellationToken);
        return await CreateViewAsync(draft, cancellationToken);
    }

    public async Task<PurchaseDetailView> UpdateAsync(EditPurchaseCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Items);
        if (command.Items.Count > MaximumItems)
        {
            throw new ArgumentException($"A purchase cannot have more than {MaximumItems} lines.", nameof(command));
        }

        var draft = await FindAsync(command.PurchaseId, cancellationToken);
        // Storage keeps whole milliseconds, so finer digits must not count as a change.
        if (command.ExpectedUpdatedAt is { } expected
            && expected.ToUnixTimeMilliseconds() != draft.UpdatedAt.ToUnixTimeMilliseconds())
        {
            throw new PurchaseConcurrencyException(draft.Id);
        }

        var itemCommands = command.Items
            .Select(item => new PurchaseItemCommand(item.Name, item.Quantity, item.Unit, item.UnitPrice, item.LineTotal, item.Kind))
            .ToList();
        // Builds and validates the lines (names, quantities, discount links)
        // before anything tracked is touched.
        var newItems = PurchaseDraftService.CreateItems(itemCommands);

        var chosenIds = new HashSet<Guid>();
        for (var position = 0; position < command.Items.Count; position++)
        {
            var item = command.Items[position];
            var line = position + 1;
            if (item.Link == ProductLinkChoice.Automatic)
            {
                continue;
            }

            if (newItems[position].IsDiscount)
            {
                throw new ArgumentException($"Line {line}: a discount line cannot be linked to a product.", nameof(command));
            }

            if (item.Link == ProductLinkChoice.Existing)
            {
                if (item.ProductId is null || item.ProductId == Guid.Empty)
                {
                    throw new ArgumentException($"Line {line}: choose the product to link to.", nameof(command));
                }

                chosenIds.Add(item.ProductId.Value);
            }
            else if (ProductNameNormalizer.Normalize(newItems[position].Name).Length == 0)
            {
                throw new ArgumentException($"Line {line}: the name has no letters or digits to name a new product.", nameof(command));
            }
        }

        var oldItems = draft.Items.OrderBy(item => item.Position).ToList();
        CarryApprovals(command.Items, newItems, oldItems);
        var oldLinks = oldItems.Select(item => item.ProductId).ToList();
        // Lines shift when one is added or removed, so a kept link is recognised
        // by product, not by position.
        var oldLinkedIds = oldLinks.OfType<Guid>().ToHashSet();
        var names = new Dictionary<Guid, string>(await catalog.GetProductNamesAsync(
            chosenIds.Concat(oldLinks.OfType<Guid>()).ToHashSet(),
            cancellationToken));
        var unknown = chosenIds.FirstOrDefault(id => !names.ContainsKey(id));
        if (unknown != Guid.Empty)
        {
            throw new ArgumentException($"Product '{unknown}' was not found.", nameof(command));
        }

        var receipt = draft.SourceReceiptId is { } receiptId
            ? await receipts.FindByIdAsync(receiptId, cancellationToken)
            : null;
        if (receipt?.Status != ReceiptProcessingStatus.Saved)
        {
            receipt = null;
        }

        var now = timeProvider.GetUtcNow();
        var actor = command.Actor ?? ReceiptActor.Unattributed;
        var receiptItems = newItems
            .Select((item, position) => ReceiptCaptureItem.Create(
                item.Position,
                item.Name,
                item.Quantity,
                item.Unit,
                item.UnitPrice,
                item.LineTotal,
                command.Items[position].BoundingBox,
                item.Kind,
                item.DiscountedItemPosition))
            .ToList();
        var auditEntries = receipt is null
            ? []
            : ReceiptReviewAuditDiffer.Diff(
                receipt,
                command.MerchantName,
                command.PurchasedAt,
                command.Currency,
                command.ReceiptTotal,
                receiptItems,
                actor,
                now).ToList();

        draft.ReplaceDetails(
            command.MerchantName,
            command.PurchasedAt,
            command.Currency,
            command.ReceiptTotal,
            newItems,
            now);

        var merchantKey = ProductNameNormalizer.NormalizeMerchant(draft.MerchantName);
        var confirmedAliases = new Dictionary<string, ProductAlias>(StringComparer.Ordinal);
        for (var position = 0; position < newItems.Count; position++)
        {
            var choice = command.Items[position];
            var item = newItems[position];
            Guid productId;
            switch (choice.Link)
            {
                case ProductLinkChoice.Existing:
                    productId = choice.ProductId!.Value;
                    break;
                case ProductLinkChoice.CreateNew:
                    var product = Product.Create(item.Name, ProductNameNormalizer.Normalize(item.Name), now);
                    catalog.AddProduct(product);
                    names[product.Id] = product.CanonicalName;
                    productId = product.Id;
                    break;
                default:
                    continue;
            }

            item.LinkToProduct(productId);
            var previous = position < oldLinks.Count ? oldLinks[position] : null;
            if (oldLinkedIds.Contains(productId))
            {
                // Keeping a link is not a new decision; it must not promote an
                // automatic alias to a human-confirmed one.
                continue;
            }

            await ConfirmAliasAsync(merchantKey, item.Name, productId, now, confirmedAliases, cancellationToken);
            if (receipt is not null && position < oldLinks.Count)
            {
                auditEntries.Add(ReceiptReviewAuditEntry.Create(
                    actor.UserId,
                    actor.Email,
                    $"Item:{position}.Product",
                    ReceiptReviewAuditChangeType.Modified,
                    previous is { } oldId && names.TryGetValue(oldId, out var oldName) ? oldName : null,
                    names[productId],
                    now));
            }
        }

        if (receipt is not null)
        {
            receipt.ApplyCorrectionAfterSave(
                command.MerchantName,
                command.PurchasedAt,
                command.Currency,
                command.ReceiptTotal,
                receiptItems,
                now);
            if (auditEntries.Count > 0)
            {
                receipt.AppendReviewAuditEntries(auditEntries);
            }
        }

        // One unit of work: purchase, receipt snapshot, audit and catalog links.
        await purchases.SaveChangesAsync(cancellationToken);

        // Lines without a person's decision go through the same matcher as a
        // fresh save; lines linked above are left alone as already linked.
        await matcher.MatchAsync(draft, cancellationToken);
        var productIds = draft.Items
            .Where(item => item.ProductId is not null)
            .Select(item => item.ProductId!.Value)
            .Where(productId => !oldLinkedIds.Contains(productId))
            .Distinct()
            .ToList();
        // Only new or changed products need categorizing; kept ones were already queued.
        if (productIds.Count > 0)
        {
            categorization.Enqueue(productIds);
        }

        return await CreateViewAsync(draft, cancellationToken);
    }

    /// <summary>
    /// A correction replaces every line row. A line that continues an approved
    /// one (by its id) stays approved even if its content changed: the person
    /// who edits a line is looking at it, and the swipe review counts an edit
    /// as a check. Each old line hands its mark to at most one new line.
    /// </summary>
    private static void CarryApprovals(
        IReadOnlyList<EditPurchaseItemCommand> commands,
        List<PurchaseItem> newItems,
        List<PurchaseItem> oldItems)
    {
        var approved = oldItems.Where(item => item.IsApproved).ToDictionary(item => item.Id);
        for (var position = 0; position < commands.Count; position++)
        {
            if (commands[position].LineId is { } lineId && approved.Remove(lineId, out var previous))
            {
                newItems[position].CarryApprovalFrom(previous);
            }
        }
    }

    private async Task ConfirmAliasAsync(
        string merchantKey,
        string rawText,
        Guid productId,
        DateTimeOffset now,
        Dictionary<string, ProductAlias> confirmed,
        CancellationToken cancellationToken)
    {
        var normalized = ProductNameNormalizer.Normalize(rawText);
        if (normalized.Length == 0)
        {
            return;
        }

        if (!confirmed.TryGetValue(normalized, out var alias))
        {
            alias = await catalog.FindAliasForUpdateAsync(merchantKey, normalized, cancellationToken);
        }

        if (alias is null)
        {
            alias = ProductAlias.Create(merchantKey, normalized, rawText, productId, ProductAliasSource.HumanConfirmed, now);
            catalog.AddAlias(alias);
        }
        else
        {
            alias.ConfirmFor(productId, rawText);
        }

        confirmed[normalized] = alias;
    }

    private async Task<PurchaseDraft> FindAsync(Guid purchaseId, CancellationToken cancellationToken)
    {
        if (purchaseId == Guid.Empty)
        {
            throw new ArgumentException("Purchase identifier cannot be empty.", nameof(purchaseId));
        }

        return await purchases.FindByIdAsync(purchaseId, cancellationToken)
            ?? throw new PurchaseDraftNotFoundException(purchaseId);
    }

    private async Task<PurchaseDetailView> CreateViewAsync(PurchaseDraft draft, CancellationToken cancellationToken)
    {
        var items = draft.Items.OrderBy(item => item.Position).ToList();
        var pending = (await catalog.ListPendingMatchesAsync(items.Select(item => item.Id).ToList(), cancellationToken))
            .ToDictionary(match => match.PurchaseItemId);
        var names = await catalog.GetProductNamesAsync(
            items.Where(item => item.ProductId is not null).Select(item => item.ProductId!.Value)
                .Concat(pending.Values.SelectMany(match => match.Candidates).Select(candidate => candidate.ProductId))
                .ToHashSet(),
            cancellationToken);

        // Photo regions live on the receipt snapshot; positions line up because
        // saving and editing both keep the two in step.
        var receipt = draft.SourceReceiptId is { } receiptId
            ? await receipts.FindByIdAsync(receiptId, cancellationToken)
            : null;
        var boxes = receipt is not null && receipt.Items.Count == items.Count
            ? receipt.Items.ToDictionary(item => item.Position, item => item.BoundingBox)
            : [];

        var views = items
            .Select(item => new PurchaseDetailItemView(
                item.Position,
                item.Name,
                item.Quantity,
                item.Unit,
                item.UnitPrice,
                item.LineTotal,
                item.Kind,
                item.DiscountedItemPosition,
                item.ProductId,
                item.ProductId is { } productId && names.TryGetValue(productId, out var name) ? name : null,
                pending.ContainsKey(item.Id),
                pending.TryGetValue(item.Id, out var match)
                    ? match.Candidates
                        .OrderByDescending(candidate => candidate.Score)
                        .ThenBy(candidate => candidate.Position)
                        .Where(candidate => names.ContainsKey(candidate.ProductId))
                        .Select(candidate => new PurchaseProductCandidateView(candidate.ProductId, names[candidate.ProductId], candidate.Score))
                        .ToList()
                    : [],
                boxes.GetValueOrDefault(item.Position),
                item.Id,
                item.ApprovedAt,
                item.ApprovedByEmail))
            .ToList();

        return new PurchaseDetailView(
            draft.Id,
            draft.SourceReceiptId,
            draft.MerchantName,
            draft.PurchasedAt,
            draft.Currency,
            draft.ReceiptTotal,
            draft.CreatedAt,
            draft.UpdatedAt,
            views,
            PurchaseDraftIssueDetector.Detect(
                draft.ReceiptTotal,
                draft.Currency,
                views.Select(item => new PurchaseDraftIssueItem(
                    item.Position, item.Name, item.UnitPrice, item.LineTotal, item.BoundingBox, item.Kind)).ToList(),
                receipt?.ReceiptTotalBoundingBox));
    }
}
