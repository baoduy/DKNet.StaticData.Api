using DKNet.StaticData.AppServices.Features.Files;
using Microsoft.EntityFrameworkCore.Storage;

namespace DKNet.StaticData.Api.ApiEndpoints.Files;

/// <summary>
/// Answers a database failure on a file route as problem details — 503 when the database cannot be reached, 500
/// otherwise (brief DRK-2209 §9 Q2) — and logs it as an error naming the database and the exception type, never the
/// exception's message, which may quote the request's values.
/// </summary>
internal sealed class DependencyFailureFilter(ILogger<DependencyFailureFilter> logger) : IEndpointFilter
{
    #region Methods

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (Exception ex) when (ex is DbUpdateException or RetryLimitExceededException or System.Data.Common.DbException
                                       or TimeoutException)
        {
            FileLog.DatabaseFailed(logger, ex.GetType().Name);
            return TypedResults.Problem(
                "The database failed. Nothing was changed.",
                statusCode: ex is DbUpdateException
                    ? StatusCodes.Status500InternalServerError
                    : StatusCodes.Status503ServiceUnavailable);
        }
    }

    #endregion
}
