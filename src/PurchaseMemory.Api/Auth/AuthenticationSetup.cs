using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace PurchaseMemory.Api.Auth;

public static class AuthenticationSetup
{
    /// <summary>Selects JwtBearer or the local bypass per request.</summary>
    public const string SelectorScheme = "PurchaseMemory";

    /// <summary>Signed in, whatever the approval state (used by <c>/api/me</c>).</summary>
    public const string SignedInPolicy = "SignedIn";

    /// <summary>Signed in and approved by the owner. The fallback for every endpoint.</summary>
    public const string ApprovedPolicy = "Approved";

    public static IServiceCollection AddPurchaseMemoryAuth(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var authOptions = configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();
        services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.SectionName));

        if (authOptions.DevelopmentBypass && !environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "Auth:DevelopmentBypass is allowed only in the Development environment. Remove it or configure Supabase.");
        }

        var bypassEnabled = authOptions.DevelopmentBypass;

        services
            .AddAuthentication(SelectorScheme)
            .AddPolicyScheme(SelectorScheme, "JWT or local development bypass", options =>
            {
                options.ForwardDefaultSelector = context =>
                    bypassEnabled && DevelopmentBypass.IsLoopbackRequest(context)
                        ? DevelopmentBypassHandler.SchemeName
                        : JwtBearerDefaults.AuthenticationScheme;
            })
            .AddScheme<AuthenticationSchemeOptions, DevelopmentBypassHandler>(DevelopmentBypassHandler.SchemeName, _ => { })
            .AddJwtBearer(options =>
            {
                // Keep Supabase's claim names ("sub", "email") as they are.
                options.MapInboundClaims = false;
                options.RequireHttpsMetadata = !environment.IsDevelopment();
                options.RefreshOnIssuerKeyNotFound = true;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = authOptions.IsSupabaseConfigured ? authOptions.Issuer : "urn:purchase-memory:auth-not-configured",
                    ValidateAudience = true,
                    ValidAudience = authOptions.Audience,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    RequireSignedTokens = true,
                    ValidateIssuerSigningKey = true,
                    // Supabase's asymmetric JWT signing keys; HS256 (shared secret) is refused.
                    ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256, SecurityAlgorithms.RsaSha256],
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = AppUser.EmailClaim,
                    RoleClaimType = "role"
                };

                if (authOptions.IsSupabaseConfigured)
                {
                    options.ConfigurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                        authOptions.JwksUrl,
                        new SupabaseJwksRetriever(authOptions.Issuer),
                        new HttpDocumentRetriever { RequireHttps = !environment.IsDevelopment() });
                }
                else
                {
                    // No project yet: bearer tokens are not even looked at, so the
                    // API answers 401 to everyone instead of trusting any key.
                    options.Configuration = new OpenIdConnectConfiguration();
                    options.Events = new JwtBearerEvents
                    {
                        OnMessageReceived = context =>
                        {
                            context.NoResult();
                            return Task.CompletedTask;
                        }
                    };
                }
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(SignedInPolicy, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(AppUser.SubjectClaim))
            .AddPolicy(ApprovedPolicy, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(AppUser.SubjectClaim)
                .RequireAssertion(context => AppUser.Status(context.User) == AppAccessStatus.Approved))
            // Secure by default: an endpoint must opt out explicitly with AllowAnonymous.
            .SetFallbackPolicy(new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .RequireClaim(AppUser.SubjectClaim)
                .RequireAssertion(context => AppUser.Status(context.User) == AppAccessStatus.Approved)
                .Build());

        services.AddSingleton<IAuthorizationMiddlewareResultHandler, AccessProblemResultHandler>();
        return services;
    }

    public static void LogAuthMode(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<AuthOptions>>().Value;
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Auth");
        if (options.DevelopmentBypass)
        {
            AuthLogMessages.DevelopmentBypassEnabled(logger);
        }

        if (options.IsSupabaseConfigured)
        {
            var providers = string.Join(", ", options.Providers);
            AuthLogMessages.SupabaseConfigured(logger, options.Issuer, providers);
        }
        else if (!options.DevelopmentBypass)
        {
            AuthLogMessages.AuthNotConfigured(logger);
        }
    }
}

internal static partial class AuthLogMessages
{
    [LoggerMessage(EventId = 7001, Level = LogLevel.Warning,
        Message = "Auth:DevelopmentBypass is ON: loopback requests to localhost are signed in as an approved local user. Never use this outside your own machine.")]
    public static partial void DevelopmentBypassEnabled(ILogger logger);

    [LoggerMessage(EventId = 7002, Level = LogLevel.Information,
        Message = "Supabase sign-in enabled. Issuer {Issuer}; providers: {Providers}")]
    public static partial void SupabaseConfigured(ILogger logger, string issuer, string providers);

    [LoggerMessage(EventId = 7003, Level = LogLevel.Warning,
        Message = "Sign-in is not configured (Auth:SupabaseUrl is empty): every API and MCP request will be refused with 401. See docs/SUPABASE_AUTH_SETUP.md.")]
    public static partial void AuthNotConfigured(ILogger logger);

    [LoggerMessage(EventId = 7004, Level = LogLevel.Information,
        Message = "Receipt upload rate limit hit for user {UserId}")]
    public static partial void UploadRateLimited(ILogger logger, string userId);
}
