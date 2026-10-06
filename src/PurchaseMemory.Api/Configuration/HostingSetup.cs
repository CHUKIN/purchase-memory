using System.Globalization;
using Microsoft.AspNetCore.HttpOverrides;
using PurchaseMemory.Api.Auth;

namespace PurchaseMemory.Api.Configuration;

/// <summary>
/// Settings for running behind a hosting proxy (Render, Fly, a reverse proxy
/// in front of Docker). Bound from the <c>ReverseProxy</c> section, e.g.
/// <c>ReverseProxy__TrustForwardedHeaders=true</c>.
/// </summary>
public sealed class ReverseProxyOptions
{
    public const string SectionName = "ReverseProxy";

    /// <summary>
    /// Read the client address and scheme from the last <c>X-Forwarded-For</c>
    /// and <c>X-Forwarded-Proto</c> entries. Turn it on only where the app is
    /// reachable solely through a proxy that appends those headers itself
    /// (Render does); otherwise any client could claim any address.
    /// </summary>
    public bool TrustForwardedHeaders { get; set; }
}

/// <summary>Port and proxy wiring for hosted (container) runs.</summary>
public static class HostingSetup
{
    /// <summary>Hosting platforms (Render, Heroku, Cloud Run) pass the port to listen on here.</summary>
    public const string PortVariable = "PORT";

    /// <summary>
    /// Listens on <c>http://0.0.0.0:$PORT</c> when <c>PORT</c> is set. Ignored in
    /// Development, where <c>launchSettings.json</c> owns the address and a stray
    /// <c>PORT</c> from another tool in the shell must not move the API away from
    /// the port the Vite proxy expects.
    /// </summary>
    public static void UseHostingPort(this WebApplicationBuilder builder)
    {
        if (builder.Environment.IsDevelopment())
        {
            return;
        }

        var port = ParsePort(builder.Configuration[PortVariable]);
        if (port is not null)
        {
            builder.WebHost.UseUrls(string.Create(CultureInfo.InvariantCulture, $"http://0.0.0.0:{port}"));
        }
    }

    /// <summary>Null when unset; a clear startup error when set to something that is not a port.</summary>
    public static int? ParsePort(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (int.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var port)
            && port is >= 1 and <= 65535)
        {
            return port;
        }

        throw new InvalidOperationException($"{PortVariable} must be a TCP port number between 1 and 65535.");
    }

    public static IServiceCollection AddReverseProxySupport(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var proxy = configuration.GetSection(ReverseProxyOptions.SectionName).Get<ReverseProxyOptions>()
            ?? new ReverseProxyOptions();
        services.Configure<ReverseProxyOptions>(configuration.GetSection(ReverseProxyOptions.SectionName));

        var bypass = configuration.GetValue<bool>($"{AuthOptions.SectionName}:{nameof(AuthOptions.DevelopmentBypass)}");
        if (proxy.TrustForwardedHeaders && bypass)
        {
            // The bypass decides on the raw TCP peer and the untouched proxy
            // headers; rewriting them first would let a forwarded request look local.
            throw new InvalidOperationException(
                "ReverseProxy:TrustForwardedHeaders cannot be combined with Auth:DevelopmentBypass.");
        }

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            // Only the entry the hosting proxy appended is believed; anything a
            // client prepended to X-Forwarded-For stays ignored.
            options.ForwardLimit = 1;
            // The proxy's own address is not published (Render uses internal
            // ranges that may change), so the peer is not checked. This is safe
            // only because the container is reachable through that proxy alone.
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
        });

        return services;
    }

    /// <summary>Must run first, before anything reads the client address or scheme.</summary>
    public static void UseReverseProxySupport(this WebApplication app)
    {
        var proxy = app.Configuration.GetSection(ReverseProxyOptions.SectionName).Get<ReverseProxyOptions>()
            ?? new ReverseProxyOptions();
        if (proxy.TrustForwardedHeaders)
        {
            app.UseForwardedHeaders();
        }
    }
}
