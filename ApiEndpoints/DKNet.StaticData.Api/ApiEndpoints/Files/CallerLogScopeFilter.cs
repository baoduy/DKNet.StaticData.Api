namespace DKNet.StaticData.Api.ApiEndpoints.Files;

/// <summary>
/// Opens one logging scope per call holding the caller id, so every entry written while the call runs names the
/// caller — the route's own entries and the dependency errors and warnings alike (spec DRK-2206 §3 Logs). The trace id
/// comes from the host's own activity scope. The owner is never put in it.
/// </summary>
internal sealed class CallerLogScopeFilter(ILogger<CallerLogScopeFilter> logger) : IEndpointFilter
{
    #region Methods

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        using (logger.BeginScope(new Dictionary<string, object?>
               {
                   ["CallerId"] = CallerAccessor.Read(context.HttpContext.User)
               }))
        {
            return await next(context);
        }
    }

    #endregion
}
