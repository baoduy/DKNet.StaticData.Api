using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace DKNet.StaticData.Api.Configs.Auth;

/// <summary>
/// Answers a caller refused for its role with 403 as problem details, as every error of the service answers; every
/// other outcome goes to ASP.NET Core's own handler. Authorization runs before any endpoint filter, so a 403 reserves
/// no idempotency key.
/// </summary>
internal sealed class ProblemDetailsForbiddenHandler : IAuthorizationMiddlewareResultHandler
{
    #region Fields

    private readonly AuthorizationMiddlewareResultHandler _default = new();

    #endregion

    #region Methods

    public Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        // Forbidden is only ever set for an authenticated caller; an unauthenticated one is challenged (401).
        if (!authorizeResult.Forbidden)
        {
            return _default.HandleAsync(next, context, policy, authorizeResult);
        }

        return TypedResults.Problem(
                "The caller does not hold the app role this route needs.",
                statusCode: StatusCodes.Status403Forbidden)
            .ExecuteAsync(context);
    }

    #endregion
}
