using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using PurchaseMemory.Api.Configuration;

namespace PurchaseMemory.IntegrationTests;

public sealed class HostingSetupTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("  ", null)]
    [InlineData("10000", 10000)]
    [InlineData(" 5099 ", 5099)]
    [InlineData("1", 1)]
    [InlineData("65535", 65535)]
    public void PortIsReadFromTheVariable(string? value, int? expected)
    {
        Assert.Equal(expected, HostingSetup.ParsePort(value));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("-1")]
    [InlineData("80a")]
    [InlineData("http://0.0.0.0:80")]
    public void PortThatIsNotAPortStopsStartup(string value)
    {
        var error = Assert.Throws<InvalidOperationException>(() => HostingSetup.ParsePort(value));
        Assert.Contains("PORT", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BehindTheProxyTheLastForwardedEntryBecomesTheClient()
    {
        await using var app = await StartProbeAsync(trustForwardedHeaders: true);
        using var client = app.GetTestClient();
        using var request = ProbeRequest(forwardedFor: "127.0.0.1, 198.51.100.7", forwardedProto: "https");

        var body = await (await client.SendAsync(request, Cancel)).Content.ReadAsStringAsync(Cancel);

        // The client-supplied "127.0.0.1" is not believed; only the proxy's own entry is.
        Assert.Equal("https 198.51.100.7", body);
    }

    [Fact]
    public async Task WithoutTheSettingForwardedHeadersAreIgnored()
    {
        await using var app = await StartProbeAsync(trustForwardedHeaders: false);
        using var client = app.GetTestClient();
        using var request = ProbeRequest(forwardedFor: "198.51.100.7", forwardedProto: "https");

        var body = await (await client.SendAsync(request, Cancel)).Content.ReadAsStringAsync(Cancel);

        Assert.Equal("http 10.0.0.5", body);
    }

    [Fact]
    public async Task TrustingProxyHeadersTogetherWithTheBypassStopsStartup()
    {
        await using var host = new AuthTestHost("Development", new Dictionary<string, string?>
        {
            ["Auth:DevelopmentBypass"] = "true",
            ["ReverseProxy:TrustForwardedHeaders"] = "true"
        });

        var error = Assert.ThrowsAny<Exception>(() => host.CreateClientFor(token: null));
        Assert.Contains("TrustForwardedHeaders", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task BehindTheProxyTheApiStillRefusesAnonymousAndForgedTokens()
    {
        await using var host = new AuthTestHost(settings: new Dictionary<string, string?>
        {
            ["ReverseProxy:TrustForwardedHeaders"] = "true"
        });
        using var anonymous = host.CreateClientFor(token: null, remoteIp: "10.0.0.5");
        anonymous.DefaultRequestHeaders.Add("X-Forwarded-For", "127.0.0.1");
        using var forged = host.CreateClientFor(
            AuthTestHost.Token(key: AuthTestHost.ForeignKey), remoteIp: "10.0.0.5");
        forged.DefaultRequestHeaders.Add("X-Forwarded-For", "127.0.0.1");

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/purchases", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await forged.GetAsync("/api/purchases", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/health", Cancel)).StatusCode);
    }

    private static HttpRequestMessage ProbeRequest(string forwardedFor, string forwardedProto)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/probe");
        request.Headers.Add("X-Forwarded-For", forwardedFor);
        request.Headers.Add("X-Forwarded-Proto", forwardedProto);
        return request;
    }

    /// <summary>A bare app with only the proxy wiring, echoing what it believes about the caller.</summary>
    private static async Task<WebApplication> StartProbeAsync(bool trustForwardedHeaders)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ReverseProxy:TrustForwardedHeaders"] = trustForwardedHeaders ? "true" : "false"
        });
        builder.Services.AddReverseProxySupport(builder.Configuration);

        var app = builder.Build();
        // TestServer has no TCP peer; pretend the request came from the proxy's address.
        app.Use((context, next) =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.5");
            return next(context);
        });
        app.UseReverseProxySupport();
        app.MapGet("/probe", (HttpContext context) =>
            $"{context.Request.Scheme} {context.Connection.RemoteIpAddress}");
        await app.StartAsync(Cancel);
        return app;
    }
}
