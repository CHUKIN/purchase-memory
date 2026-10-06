using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using PurchaseMemory.Api.Auth;

namespace PurchaseMemory.IntegrationTests;

public sealed class AuthApiTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ApprovedUserReachesTheApi()
    {
        await using var host = new AuthTestHost();
        using var client = host.CreateClientFor(AuthTestHost.Token());

        var response = await client.GetAsync("/api/purchases", Cancel);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/purchases")]
    [InlineData("/api/products")]
    [InlineData("/api/categories")]
    [InlineData("/api/products/00000000-0000-4000-8000-000000000001/audit")]
    [InlineData("/api/receipts")]
    [InlineData("/api/recognition/status")]
    [InlineData("/api/me")]
    public async Task AnonymousRequestsAreRefused(string path)
    {
        await using var host = new AuthTestHost();
        using var client = host.CreateClientFor(token: null);

        var response = await client.GetAsync(path, Cancel);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ExpiredTokenIsRefused()
    {
        await using var host = new AuthTestHost();
        using var client = host.CreateClientFor(AuthTestHost.Token(expires: DateTime.UtcNow.AddMinutes(-5)));

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/purchases", Cancel)).StatusCode);
    }

    [Fact]
    public async Task TokenSignedByAnotherKeyIsRefused()
    {
        await using var host = new AuthTestHost();
        using var client = host.CreateClientFor(AuthTestHost.Token(key: AuthTestHost.ForeignKey));

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/purchases", Cancel)).StatusCode);
    }

    [Fact]
    public async Task TokenFromAnotherProjectIsRefused()
    {
        await using var host = new AuthTestHost();
        using var client = host.CreateClientFor(AuthTestHost.Token(issuer: "https://other.supabase.co/auth/v1"));

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/purchases", Cancel)).StatusCode);
    }

    [Fact]
    public async Task AnonKeyStyleAudienceIsRefused()
    {
        await using var host = new AuthTestHost();
        using var client = host.CreateClientFor(AuthTestHost.Token(audience: "anon"));

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/purchases", Cancel)).StatusCode);
    }

    [Theory]
    [InlineData("pending", "pending")]
    [InlineData(null, "pending")]
    [InlineData("something-else", "pending")]
    [InlineData("blocked", "blocked")]
    public async Task NotApprovedUserIsForbiddenAndToldWhy(string? access, string expectedStatus)
    {
        await using var host = new AuthTestHost();
        using var client = host.CreateClientFor(AuthTestHost.Token(access: access));

        var response = await client.GetAsync("/api/purchases", Cancel);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancel));
        Assert.Equal(expectedStatus, problem.RootElement.GetProperty("accessStatus").GetString());
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("approved")]
    [InlineData("blocked")]
    public async Task CurrentUserEndpointReportsTheAccessStatus(string access)
    {
        await using var host = new AuthTestHost();
        using var client = host.CreateClientFor(AuthTestHost.Token(access: access));

        var me = await client.GetFromJsonAsync<CurrentUserResponse>("/api/me", Cancel);

        Assert.NotNull(me);
        Assert.Equal(access, me.Status);
        Assert.Equal("anna@example.com", me.Email);
        Assert.Equal("11111111-1111-1111-1111-111111111111", me.Id);
    }

    [Fact]
    public async Task McpEndpointRequiresAnApprovedUser()
    {
        await using var host = new AuthTestHost();
        var initialize = """{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}""";

        using var anonymous = host.CreateClientFor(token: null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostMcpAsync(anonymous, initialize)).StatusCode);

        using var pending = host.CreateClientFor(AuthTestHost.Token(access: "pending"));
        Assert.Equal(HttpStatusCode.Forbidden, (await PostMcpAsync(pending, initialize)).StatusCode);

        using var approved = host.CreateClientFor(AuthTestHost.Token());
        var allowed = await PostMcpAsync(approved, initialize);
        Assert.NotEqual(HttpStatusCode.Unauthorized, allowed.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, allowed.StatusCode);
    }

    [Fact]
    public async Task SpaShellHealthAndAuthConfigArePublic()
    {
        await using var host = new AuthTestHost(settings: new Dictionary<string, string?>
        {
            ["Auth:Providers:0"] = "email",
            ["Auth:Providers:1"] = "Google"
        });
        using var client = host.CreateClientFor(token: null);

        var spa = await client.GetAsync("/purchases", Cancel);
        Assert.Equal(HttpStatusCode.OK, spa.StatusCode);
        Assert.Equal("text/html", spa.Content.Headers.ContentType?.MediaType);

        var health = await client.GetStringAsync("/health", Cancel);
        Assert.Equal("""{"status":"healthy"}""", health);

        var config = await client.GetFromJsonAsync<AuthConfigResponse>("/api/auth/config", Cancel);
        Assert.NotNull(config);
        Assert.Equal("supabase", config.Mode);
        Assert.Equal(AuthTestHost.SupabaseUrl, config.SupabaseUrl);
        Assert.Equal("sb_publishable_test", config.SupabasePublishableKey);
        Assert.Equal(["email", "google"], config.Providers);
        Assert.False(config.DevelopmentBypass);
    }

    // An iPhone installs the Home Screen app before anyone signs in, so the
    // manifest, icons and service worker must be public files with their real
    // types, never a 401 or the SPA page. The test serves stub files from its
    // own web root, so it does not depend on a frontend build in wwwroot.
    [Theory]
    [InlineData("manifest.webmanifest", "application/manifest+json")]
    [InlineData("apple-touch-icon.png", "image/png")]
    [InlineData("icons/icon-512-maskable.png", "image/png")]
    [InlineData("sw.js", "text/javascript")]
    public async Task HomeScreenInstallFilesArePublicWithTheirOwnType(string path, string mediaType)
    {
        var webRoot = Path.Combine(Path.GetTempPath(), "purchase-memory-webroot-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var file = Path.Combine(webRoot, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            await File.WriteAllTextAsync(file, "stub", Cancel);
            await File.WriteAllTextAsync(Path.Combine(webRoot, "index.html"), "<!doctype html>", Cancel);

            await using var host = new AuthTestHost(settings: new Dictionary<string, string?> { [WebHostDefaults.WebRootKey] = webRoot });
            using var client = host.CreateClientFor(token: null);

            var response = await client.GetAsync("/" + path, Cancel);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(mediaType, response.Content.Headers.ContentType?.MediaType);
            Assert.Equal("stub", await response.Content.ReadAsStringAsync(Cancel));
        }
        finally
        {
            if (Directory.Exists(webRoot)) Directory.Delete(webRoot, recursive: true);
        }
    }

    [Fact]
    public async Task WithoutSupabaseAndWithoutBypassEverythingStaysClosed()
    {
        await using var host = new AuthTestHost("Development", new Dictionary<string, string?> { ["Auth:SupabaseUrl"] = "" });
        using var client = host.CreateClientFor(AuthTestHost.Token(), remoteIp: "127.0.0.1");

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/purchases", Cancel)).StatusCode);
        var config = await client.GetFromJsonAsync<AuthConfigResponse>("/api/auth/config", Cancel);
        Assert.Equal("unconfigured", config!.Mode);
    }

    [Fact]
    public async Task UploadsAreRateLimitedPerUser()
    {
        await using var host = new AuthTestHost(settings: new Dictionary<string, string?>
        {
            ["RateLimiting:ReceiptUploads:TokenLimit"] = "2",
            ["RateLimiting:ReceiptUploads:ReplenishmentPeriodSeconds"] = "3600"
        });
        using var anna = host.CreateClientFor(AuthTestHost.Token());
        using var boris = host.CreateClientFor(AuthTestHost.Token(subject: "22222222-2222-2222-2222-222222222222"));

        Assert.NotEqual(HttpStatusCode.TooManyRequests, (await UploadJunkAsync(anna)).StatusCode);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, (await UploadJunkAsync(anna)).StatusCode);
        var limited = await UploadJunkAsync(anna);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        using var problem = JsonDocument.Parse(await limited.Content.ReadAsStringAsync(Cancel));
        Assert.True(problem.RootElement.GetProperty("retryAfterSeconds").GetInt32() > 0);

        Assert.NotEqual(HttpStatusCode.TooManyRequests, (await UploadJunkAsync(boris)).StatusCode);
    }

    [Fact]
    public async Task BypassSignsInLoopbackRequestsInDevelopment()
    {
        await using var host = new AuthTestHost("Development", new Dictionary<string, string?>
        {
            ["Auth:SupabaseUrl"] = "",
            ["Auth:DevelopmentBypass"] = "true"
        });
        using var local = host.CreateClientFor(token: null, remoteIp: "127.0.0.1");

        Assert.Equal(HttpStatusCode.OK, (await local.GetAsync("/api/purchases", Cancel)).StatusCode);
        var me = await local.GetFromJsonAsync<CurrentUserResponse>("/api/me", Cancel);
        Assert.Equal("approved", me!.Status);
        Assert.Equal(DevelopmentBypassHandler.UserId, me.Id);
        var config = await local.GetFromJsonAsync<AuthConfigResponse>("/api/auth/config", Cancel);
        Assert.Equal("developmentBypass", config!.Mode);
    }

    [Fact]
    public async Task BypassIgnoresRequestsFromTheNetwork()
    {
        await using var host = new AuthTestHost("Development", new Dictionary<string, string?>
        {
            ["Auth:SupabaseUrl"] = "",
            ["Auth:DevelopmentBypass"] = "true"
        });
        using var lan = host.CreateClientFor(token: null, remoteIp: "192.168.1.20");
        Assert.Equal(HttpStatusCode.Unauthorized, (await lan.GetAsync("/api/purchases", Cancel)).StatusCode);

        // A LAN client going through the Vite proxy: loopback peer, LAN Host header.
        using var proxied = host.CreateClientFor(token: null, remoteIp: "127.0.0.1");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/purchases");
        request.Headers.Host = "192.168.1.5:5173";
        Assert.Equal(HttpStatusCode.Unauthorized, (await proxied.SendAsync(request, Cancel)).StatusCode);
    }

    [Theory]
    // What vite's proxy (xfwd: true) sends for a LAN client: peer is loopback,
    // Host is whatever the client chose, and the real address is appended.
    [InlineData("192.168.0.180")]
    // A LAN client forging a loopback entry: the proxy still appends its real address.
    [InlineData("127.0.0.1,192.168.0.180")]
    public async Task BypassRefusesALanClientComingThroughTheViteProxy(string forwardedFor)
    {
        await using var host = new AuthTestHost("Development", new Dictionary<string, string?>
        {
            ["Auth:SupabaseUrl"] = "",
            ["Auth:DevelopmentBypass"] = "true"
        });
        using var proxied = host.CreateClientFor(token: null, remoteIp: "127.0.0.1");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        request.Headers.Host = "localhost:5173";
        request.Headers.Add("X-Forwarded-For", forwardedFor);
        request.Headers.Add("X-Forwarded-Host", "localhost:5173");

        Assert.Equal(HttpStatusCode.Unauthorized, (await proxied.SendAsync(request, Cancel)).StatusCode);
    }

    [Fact]
    public async Task BypassStillWorksForThisMachineThroughTheViteProxy()
    {
        await using var host = new AuthTestHost("Development", new Dictionary<string, string?>
        {
            ["Auth:SupabaseUrl"] = "",
            ["Auth:DevelopmentBypass"] = "true"
        });
        using var proxied = host.CreateClientFor(token: null, remoteIp: "127.0.0.1");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        request.Headers.Host = "localhost:5173";
        request.Headers.Add("X-Forwarded-For", "::ffff:127.0.0.1");

        Assert.Equal(HttpStatusCode.OK, (await proxied.SendAsync(request, Cancel)).StatusCode);
    }

    [Theory]
    [InlineData("Forwarded", "for=192.168.0.180")]
    [InlineData("Via", "1.1 some-proxy")]
    public async Task BypassRefusesRequestsMarkedByOtherProxies(string header, string value)
    {
        await using var host = new AuthTestHost("Development", new Dictionary<string, string?>
        {
            ["Auth:SupabaseUrl"] = "",
            ["Auth:DevelopmentBypass"] = "true"
        });
        using var client = host.CreateClientFor(token: null, remoteIp: "127.0.0.1");
        client.DefaultRequestHeaders.TryAddWithoutValidation(header, value);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me", Cancel)).StatusCode);
    }

    [Theory]
    [InlineData("/api/does-not-exist")]
    [InlineData("/api")]
    [InlineData("/mcp/anything")]
    public async Task UnknownApiAndMcpPathsAre404NotTheSpa(string path)
    {
        await using var host = new AuthTestHost();
        using var client = host.CreateClientFor(AuthTestHost.Token());

        var response = await client.GetAsync(path, Cancel);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task McpWithAnUnsupportedMethodIsNotTheSpa()
    {
        await using var host = new AuthTestHost();
        using var client = host.CreateClientFor(AuthTestHost.Token());

        var response = await client.PutAsync("/mcp", new StringContent("{}"), Cancel);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task BypassOutsideDevelopmentStopsStartup()
    {
        await using var host = new AuthTestHost("Production", new Dictionary<string, string?>
        {
            ["Auth:DevelopmentBypass"] = "true"
        });

        var error = Assert.ThrowsAny<Exception>(() => host.CreateClientFor(token: null));
        Assert.Contains("DevelopmentBypass", error.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("127.0.0.1", "localhost", true)]
    [InlineData("::1", "[::1]", true)]
    [InlineData("127.0.0.1", "127.0.0.1", true)]
    [InlineData("127.0.0.1", "purchase.example.com", false)]
    [InlineData("10.0.0.3", "localhost", false)]
    public void LoopbackRuleNeedsBothPeerAndHost(string remoteIp, string host, bool expected)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);
        context.Request.Host = new HostString(host);

        Assert.Equal(expected, DevelopmentBypass.IsLoopbackRequest(context));
    }

    private static Task<HttpResponseMessage> PostMcpAsync(HttpClient client, string body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        return client.SendAsync(request, Cancel);
    }

    private static Task<HttpResponseMessage> UploadJunkAsync(HttpClient client)
    {
        var form = new MultipartFormDataContent();
        var image = new ByteArrayContent([1, 2, 3, 4]);
        image.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        form.Add(image, "images", "receipt.jpg");
        return client.PostAsync("/api/receipts", form, Cancel);
    }
}

public sealed class SupabaseJwksRetrieverTests
{
    [Fact]
    public async Task PublicKeysFromTheJwksDocumentBecomeSigningKeys()
    {
        var parameters = AuthTestHost.SigningKey.ExportParameters(includePrivateParameters: false);
        var jwks = $$"""
            {"keys":[{"kty":"EC","crv":"P-256","alg":"ES256","use":"sig","kid":"k1",
              "x":"{{Microsoft.IdentityModel.Tokens.Base64UrlEncoder.Encode(parameters.Q.X)}}",
              "y":"{{Microsoft.IdentityModel.Tokens.Base64UrlEncoder.Encode(parameters.Q.Y)}}"}]}
            """;
        var retriever = new SupabaseJwksRetriever(AuthTestHost.Issuer);

        var configuration = await retriever.GetConfigurationAsync(
            AuthTestHost.Issuer + "/.well-known/jwks.json",
            new FixedDocumentRetriever(jwks),
            TestContext.Current.CancellationToken);

        Assert.Equal(AuthTestHost.Issuer, configuration.Issuer);
        var key = Assert.Single(configuration.SigningKeys);
        Assert.Equal("k1", key.KeyId);
    }

    private sealed class FixedDocumentRetriever(string document) : Microsoft.IdentityModel.Protocols.IDocumentRetriever
    {
        public Task<string> GetDocumentAsync(string address, CancellationToken cancel) => Task.FromResult(document);
    }
}
