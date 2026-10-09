using HealthChecks.UI.Client;
using DKNet.StaticData.Infra.Contexts;

namespace DKNet.StaticData.Api.Configs.Healthz;

[ExcludeFromCodeCoverage]
internal static class HealthzConfig
{
    #region Methods

    public static IServiceCollection AddHealthzConfig(this IServiceCollection services, FeatureOptions features)
    {
        if (!features.EnableHealthCheck)
        {
            return services;
        }

        // The database is the only check. The default test query (CanConnectAsync) swallows the connect
        // exception, so the detail report would show no failure text; this one lets it surface.
        services.AddHealthChecks()
            .AddDbContextCheck<CoreDbContext>(customTestQuery: PingDatabaseAsync);
        services.MarkConfigAdded(nameof(HealthzConfig));
        return services;
    }

    /// <summary>
    ///     The health check endpoint will be "/healthz"
    /// </summary>
    /// <param name="endpoints"></param>
    /// <returns></returns>
    public static WebApplication UseHealthzConfig(this WebApplication endpoints)
    {
        if (!endpoints.Services.IsConfigAdded(nameof(HealthzConfig)))
        {
            return endpoints;
        }

        // Public surface: status only, no check name/duration/description/exception text (R4) — anonymous by
        // design, so it must never leak dependency detail to an unauthenticated caller.
        var publicOptions = new HealthCheckOptions
        {
            AllowCachingResponses = false,
            Predicate = _ => true,
            ResponseWriter = WriteStatusOnlyResponse
        };
        endpoints.MapHealthChecks("/healthz", publicOptions).AllowAnonymous();

        // Detailed surface: the per-check report, anonymous like the status route (DRK-2198 R4) — the only other
        // route the FallbackPolicy lets through without a token.
        var detailOptions = new HealthCheckOptions
        {
            AllowCachingResponses = false,
            Predicate = _ => true,
            ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
        };
        endpoints.MapHealthChecks("/healthz/detail", detailOptions).AllowAnonymous();

        endpoints.Logger.LogInformation("{Feature} enabled", nameof(HealthzConfig));

        return endpoints;
    }

    /// <summary>
    ///     Runs one round trip on a raw connection, outside EF Core's retrying execution strategy, so a stopped
    ///     database fails the check at once and its exception reaches the report. A pooled connection alone is no
    ///     proof: opening one does not touch the server.
    /// </summary>
    private static async Task<bool> PingDatabaseAsync(CoreDbContext db, CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            await command.ExecuteScalarAsync(cancellationToken);
            return true;
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    private static Task WriteStatusOnlyResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsync($$"""{"status":"{{report.Status}}"}""");
    }

    #endregion
}