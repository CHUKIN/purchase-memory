using Microsoft.Extensions.Options;

namespace PurchaseMemory.Api.Auth;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Public: the login screen needs it before anyone is signed in. It only
        // exposes values the browser must know anyway.
        endpoints.MapGet("/api/auth/config", (IOptions<AuthOptions> options, IHostEnvironment environment) =>
            {
                var auth = options.Value;
                var mode = auth.IsSupabaseConfigured
                    ? "supabase"
                    : auth.DevelopmentBypass && environment.IsDevelopment() ? "developmentBypass" : "unconfigured";
                return Results.Ok(new AuthConfigResponse(
                    mode,
                    auth.IsSupabaseConfigured ? auth.NormalizedSupabaseUrl : null,
                    auth.IsSupabaseConfigured ? auth.SupabasePublishableKey : null,
                    auth.Providers
                        .Select(provider => provider.Trim().ToLowerInvariant())
                        .Where(provider => provider.Length > 0)
                        .Distinct()
                        .ToList(),
                    auth.DevelopmentBypass && environment.IsDevelopment()));
            })
            .AllowAnonymous()
            .WithName("GetAuthConfig");

        // Signed in but possibly not approved yet: tells the PWA which screen to show.
        endpoints.MapGet("/api/me", (HttpContext context) =>
            {
                var user = context.User;
                return Results.Ok(new CurrentUserResponse(
                    AppUser.UserId(user)!,
                    AppUser.Email(user),
                    AppUser.ToWire(AppUser.Status(user))));
            })
            .RequireAuthorization(AuthenticationSetup.SignedInPolicy)
            .WithName("GetCurrentUser");

        return endpoints;
    }
}

/// <param name="Mode">"supabase", "developmentBypass" or "unconfigured".</param>
/// <param name="DevelopmentBypass">
/// True when the server may let loopback requests in without a token; the PWA
/// then tries <c>/api/me</c> before showing the login screen.
/// </param>
public sealed record AuthConfigResponse(
    string Mode,
    string? SupabaseUrl,
    string? SupabasePublishableKey,
    IReadOnlyList<string> Providers,
    bool DevelopmentBypass);

public sealed record CurrentUserResponse(string Id, string? Email, string Status);
