using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace PurchaseMemory.Api.Auth;

/// <summary>
/// Turns a 403 for a signed-in but not approved user into a problem response
/// that names the access state, so the PWA can show "waiting for approval" or
/// "blocked" instead of a generic error.
/// </summary>
public sealed class AccessProblemResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden && context.User.Identity?.IsAuthenticated == true)
        {
            var status = AppUser.Status(context.User);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await Results.Problem(
                    title: "Access is not approved",
                    detail: status == AppAccessStatus.Blocked
                        ? "Access for this account is blocked."
                        : "This account is waiting for approval by the owner.",
                    statusCode: StatusCodes.Status403Forbidden,
                    extensions: new Dictionary<string, object?> { ["accessStatus"] = AppUser.ToWire(status) })
                .ExecuteAsync(context);
            return;
        }

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }
}
