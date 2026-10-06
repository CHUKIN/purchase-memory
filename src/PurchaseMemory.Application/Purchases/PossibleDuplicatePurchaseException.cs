namespace PurchaseMemory.Application.Purchases;

public sealed class PossibleDuplicatePurchaseException(Guid existingPurchaseId)
    : Exception(
        $"A matching purchase already exists with purchaseId '{existingPurchaseId}'. " +
        "Do not save it again. If the user confirms this is a separate purchase, " +
        "retry create_purchase_draft with allowPossibleDuplicate set to true.")
{
    public Guid ExistingPurchaseId { get; } = existingPurchaseId;
}
