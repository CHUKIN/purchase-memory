using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace PurchaseMemory.Api.Auth;

/// <summary>
/// Loads Supabase's public signing keys straight from the JWKS endpoint, so
/// the JwtBearer <see cref="ConfigurationManager{T}"/> can cache them and
/// refetch when a token arrives with an unknown <c>kid</c> (key rotation).
/// Only public asymmetric keys are published there; the legacy shared HS256
/// secret is deliberately not supported, so no secret is ever configured here.
/// </summary>
public sealed class SupabaseJwksRetriever(string issuer) : IConfigurationRetriever<OpenIdConnectConfiguration>
{
    public async Task<OpenIdConnectConfiguration> GetConfigurationAsync(
        string address,
        IDocumentRetriever retriever,
        CancellationToken cancel)
    {
        var json = await retriever.GetDocumentAsync(address, cancel);
        var keys = new JsonWebKeySet(json);
        var configuration = new OpenIdConnectConfiguration { Issuer = issuer, JwksUri = address };
        foreach (var key in keys.GetSigningKeys())
        {
            configuration.SigningKeys.Add(key);
        }

        return configuration;
    }
}
