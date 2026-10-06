using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace PurchaseMemory.IntegrationTests;

/// <summary>
/// The real API host on a throwaway data directory. Supabase is replaced by a
/// local ES256 key served through a static JWKS configuration, so no test
/// touches the network or the repository's <c>data/</c>.
/// </summary>
internal sealed class AuthTestHost : WebApplicationFactory<Program>
{
    public const string SupabaseUrl = "https://test-project.supabase.co";
    public const string Issuer = SupabaseUrl + "/auth/v1";
    public const string RemoteIpHeader = "X-Test-Remote-Ip";

    private readonly string _directory;
    private readonly Dictionary<string, string?> _settings;
    private readonly string _environment;
    private readonly PostgresTestDatabase? _postgres;
    private readonly PurchaseMemory.Infrastructure.Images.S3StorageOptions? _s3;
    private readonly Action<IServiceCollection>? _configureServices;

    public AuthTestHost(
        string environment = "Production",
        IDictionary<string, string?>? settings = null,
        Action<IServiceCollection>? configureServices = null)
    {
        _environment = environment;
        _configureServices = configureServices;
        _directory = Path.Combine(Path.GetTempPath(), "purchase-memory-auth-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _settings = new Dictionary<string, string?>
        {
            ["Paths:DataDirectory"] = Path.Combine(_directory, "data"),
            ["Paths:LogDirectory"] = Path.Combine(_directory, "logs"),
            ["Auth:SupabaseUrl"] = SupabaseUrl,
            ["Auth:SupabasePublishableKey"] = "sb_publishable_test",
            // Never a paid model call from a test, even if OPENAI_API_KEY is set.
            ["Categorization:Enabled"] = "false",
            ["Analytics:Enabled"] = "false"
        };
        // Opt-in real backends (see ExternalTestServices); explicit settings win.
        _postgres = PostgresTestDatabase.TryCreate();
        if (_postgres is not null)
        {
            _settings["Database:Provider"] = "Postgres";
            _settings["ConnectionStrings:Postgres"] = _postgres.ConnectionString;
        }

        _s3 = ExternalTestServices.S3Options(keyPrefix: Guid.NewGuid().ToString("N"));
        if (_s3 is not null)
        {
            ExternalTestServices.EnsureBucketAsync(_s3).GetAwaiter().GetResult();
            _settings["Storage:Provider"] = "S3";
            _settings["Storage:S3:ServiceUrl"] = _s3.ServiceUrl;
            _settings["Storage:S3:Region"] = _s3.Region;
            _settings["Storage:S3:Bucket"] = _s3.Bucket;
            _settings["Storage:S3:KeyPrefix"] = _s3.KeyPrefix;
            _settings["Storage:S3:AccessKeyId"] = _s3.AccessKeyId;
            _settings["Storage:S3:SecretAccessKey"] = _s3.SecretAccessKey;
        }

        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            _settings[key] = value;
        }
    }

    public static ECDsa SigningKey { get; } = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public static ECDsa ForeignKey { get; } = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public string DataDirectory => Path.Combine(_directory, "data");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);
        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
                configuration.SigningKeys.Add(new ECDsaSecurityKey(SigningKey) { KeyId = "test-key" });
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
            });
            services.AddSingleton<IStartupFilter, RemoteIpStartupFilter>();
            _configureServices?.Invoke(services);
        });
    }

    public HttpClient CreateClientFor(string? token, string remoteIp = "203.0.113.10")
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(RemoteIpHeader, remoteIp);
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    public static string Token(
        string? access = "approved",
        string subject = "11111111-1111-1111-1111-111111111111",
        string? email = "anna@example.com",
        DateTime? expires = null,
        string issuer = Issuer,
        string audience = "authenticated",
        ECDsa? key = null)
    {
        var claims = new Dictionary<string, object> { ["sub"] = subject, ["role"] = "authenticated" };
        if (email is not null) claims["email"] = email;
        if (access is not null) claims["app_access"] = access;
        var now = DateTime.UtcNow;
        var expiresAt = expires ?? now.AddMinutes(30);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Claims = claims,
            IssuedAt = expiresAt.AddMinutes(-60),
            NotBefore = expiresAt.AddMinutes(-60),
            Expires = expiresAt,
            SigningCredentials = new SigningCredentials(
                new ECDsaSecurityKey(key ?? SigningKey) { KeyId = "test-key" },
                SecurityAlgorithms.EcdsaSha256)
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }

        if (_s3 is not null)
        {
            await ExternalTestServices.DeletePrefixAsync(_s3);
        }

        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // SQLite may release the file a moment later; the OS temp cleanup handles leftovers.
        }
    }

    /// <summary>TestServer has no TCP peer; tests declare one through a header.</summary>
    private sealed class RemoteIpStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress =
                    IPAddress.TryParse(context.Request.Headers[RemoteIpHeader], out var address) ? address : null;
                return nextMiddleware(context);
            });
            next(app);
        };
    }
}
