namespace PurchaseMemory.Application.Receipts;

/// <summary>
/// Actor recorded on audit entries written before sign-in existed, and the
/// fallback for internal callers (tests, tools) that act without a user.
/// </summary>
public static class ReceiptReviewActor
{
    public const string Stub = "Owner";
}

/// <summary>
/// The signed-in person behind a receipt upload or review. The identifier is
/// the stable Supabase user id (<c>sub</c>); the email is a readable copy for
/// the audit trail and may be absent (for example for some OAuth providers).
/// </summary>
public sealed record ReceiptActor(string UserId, string? Email)
{
    public static ReceiptActor Unattributed { get; } = new(ReceiptReviewActor.Stub, null);
}
