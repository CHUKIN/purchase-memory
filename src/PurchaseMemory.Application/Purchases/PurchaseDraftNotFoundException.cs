namespace PurchaseMemory.Application.Purchases;

public sealed class PurchaseDraftNotFoundException(Guid purchaseId)
    : Exception($"Purchase draft '{purchaseId}' was not found.")
{
    public Guid PurchaseId { get; } = purchaseId;
}
