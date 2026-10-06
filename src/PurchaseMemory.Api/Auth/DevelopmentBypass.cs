using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace PurchaseMemory.Api.Auth;

/// <summary>
/// Signs every eligible request in as a fixed approved local user. Which
/// requests are eligible is decided by <see cref="DevelopmentBypass.IsLoopbackRequest"/>.
/// </summary>
public sealed class DevelopmentBypassHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "DevelopmentBypass";
    public const string UserId = "local-development-user";
    public const string Email = "dev@localhost";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(AppUser.SubjectClaim, UserId),
                new Claim(AppUser.EmailClaim, Email),
                new Claim(AppUser.AccessClaim, "approved")
            ],
            SchemeName,
            AppUser.EmailClaim,
            "role");
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

/// <summary>
/// The bypass must only ever reach a person sitting at this machine.
/// A TCP peer of 127.0.0.1 is not enough: the Vite dev server listens on the
/// LAN and forwards to the API over loopback, and it keeps (or lets the
/// client choose) the Host header, so the Host header proves nothing either.
/// What cannot be forged from the LAN is the proxy's own record of the real
/// client: vite.config.ts sets <c>xfwd: true</c>, and the proxy then
/// <em>appends</em> the true peer address to <c>X-Forwarded-For</c>. A LAN
/// client can prepend fake loopback entries but can never remove the real
/// one the proxy adds last. So a request is eligible only when the TCP peer
/// is loopback, the Host names a loopback host, there is no
/// <c>Forwarded</c>/<c>Via</c> header (other proxies), and every
/// <c>X-Forwarded-For</c> entry, if any, is a loopback address.
/// </summary>
public static class DevelopmentBypass
{
    public static bool IsLoopbackRequest(HttpContext context)
    {
        var remote = context.Connection.RemoteIpAddress;
        if (remote is null || !IPAddress.IsLoopback(remote))
        {
            return false;
        }

        var headers = context.Request.Headers;
        if (headers.ContainsKey("Forwarded") || headers.ContainsKey("Via"))
        {
            return false;
        }

        foreach (var value in headers["X-Forwarded-For"])
        {
            foreach (var entry in (value ?? string.Empty).Split(',', StringSplitOptions.TrimEntries))
            {
                if (!IsLoopbackAddress(entry))
                {
                    return false;
                }
            }
        }

        var host = context.Request.Host.Host;
        return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) || IsLoopbackAddress(host);
    }

    private static bool IsLoopbackAddress(string value)
    {
        var text = value.Trim().Trim('[', ']');
        return IPAddress.TryParse(text, out var address)
            && (IPAddress.IsLoopback(address) || (address.IsIPv4MappedToIPv6 && IPAddress.IsLoopback(address.MapToIPv4())));
    }
}
