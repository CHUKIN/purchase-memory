using System.Security.Claims;
using PurchaseMemory.Application.Receipts;

namespace PurchaseMemory.Api.Auth;

/// <summary>Access state an owner assigns in the Supabase <c>app_users</c> table.</summary>
public enum AppAccessStatus
{
    Pending,
    Approved,
    Blocked
}

/// <summary>
/// Reads the caller from the validated principal. Claim names are Supabase's
/// raw JWT names because inbound claim mapping is switched off.
/// </summary>
public static class AppUser
{
    public const string SubjectClaim = "sub";
    public const string EmailClaim = "email";

    /// <summary>
    /// Written by the Custom Access Token hook from <c>public.app_users.status</c>.
    /// It lives outside <c>user_metadata</c>, which users can edit themselves.
    /// </summary>
    public const string AccessClaim = "app_access";

    public static string? UserId(ClaimsPrincipal user) => user.FindFirst(SubjectClaim)?.Value;

    public static string? Email(ClaimsPrincipal user) => user.FindFirst(EmailClaim)?.Value;

    /// <summary>
    /// A missing or unknown value is treated as pending: if the hook is not
    /// installed yet nobody is let in, rather than everybody.
    /// </summary>
    public static AppAccessStatus Status(ClaimsPrincipal user) =>
        user.FindFirst(AccessClaim)?.Value?.Trim().ToLowerInvariant() switch
        {
            "approved" => AppAccessStatus.Approved,
            "blocked" => AppAccessStatus.Blocked,
            _ => AppAccessStatus.Pending
        };

    public static string ToWire(AppAccessStatus status) => status switch
    {
        AppAccessStatus.Approved => "approved",
        AppAccessStatus.Blocked => "blocked",
        _ => "pending"
    };

    public static ReceiptActor Actor(ClaimsPrincipal user) =>
        new(UserId(user) ?? throw new InvalidOperationException("Authenticated user has no subject."), Email(user));
}
