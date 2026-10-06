namespace PurchaseMemory.Api.Auth;

/// <summary>
/// Sign-in settings. Values come from configuration or environment variables
/// (<c>Auth__SupabaseUrl</c>, <c>Auth__SupabasePublishableKey</c>, ...); the
/// committed appsettings keep them empty. Both Supabase values are public by
/// design (the browser needs them), but they still stay out of the repository
/// so every environment points at its own project.
/// </summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>Project URL, e.g. <c>https://abcd.supabase.co</c>.</summary>
    public string SupabaseUrl { get; set; } = string.Empty;

    /// <summary>Publishable (or legacy anon) key handed to supabase-js.</summary>
    public string SupabasePublishableKey { get; set; } = string.Empty;

    /// <summary>
    /// Sign-in methods the login screen offers: <c>email</c> for the emailed
    /// link, plus OAuth provider ids enabled in the Supabase dashboard
    /// (<c>google</c>, <c>apple</c>, <c>github</c>, ...).
    /// </summary>
    public string[] Providers { get; set; } = ["email"];

    /// <summary>JWT audience Supabase puts on signed-in user tokens.</summary>
    public string Audience { get; set; } = "authenticated";

    /// <summary>
    /// Local-only escape hatch while no Supabase project exists. Honoured only
    /// in the Development environment and only for requests that arrive over
    /// loopback addressed to a loopback host; startup fails anywhere else.
    /// </summary>
    public bool DevelopmentBypass { get; set; }

    public bool IsSupabaseConfigured => !string.IsNullOrWhiteSpace(SupabaseUrl);

    public string NormalizedSupabaseUrl => SupabaseUrl.Trim().TrimEnd('/');

    /// <summary>Issuer Supabase writes into the <c>iss</c> claim.</summary>
    public string Issuer => $"{NormalizedSupabaseUrl}/auth/v1";

    public string JwksUrl => $"{Issuer}/.well-known/jwks.json";
}
