using DKNet.AspCore.Idempotency;
using DKNet.AspCore.Idempotency.MsSqlStore;
using DKNet.AspCore.Idempotency.NpgsqlStore;
using JsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace DKNet.StaticData.Api.Configs;

/// <summary>
/// Idempotency records in the service's own database, in the DKNet store for its <see cref="DatabaseProvider" />
/// (ADR-0012). The package creates and migrates its own table; this service's hourly sweep deletes expired rows.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Registration only; the key scope logic is IdempotencyKeyScope, which is measured.")]
internal static class IdempotencyConfig
{
    #region Methods

    public static IServiceCollection AddIdempotencyConfig(
        this IServiceCollection services,
        IConfiguration configuration,
        DatabaseProvider database)
    {
        var connectionString = configuration.GetConnectionString(SharedConsts.DbConnectionString)!;
        _ = database switch
        {
            DatabaseProvider.Postgres => services.AddIdempotencyWithNpgsqlStore(connectionString, Configure),
            DatabaseProvider.SqlServer => services.AddIdempotencyWithMsSqlStore(connectionString, Configure),
            _ => throw new ArgumentOutOfRangeException(nameof(database), database, "Unknown database.")
        };

        // A replay must carry the first answer's body byte for byte, so the kept body is written with the same JSON
        // settings the route answered with (camel case, no null field).
        services.AddOptions<IdempotencyOptions>()
            .Configure<IOptions<JsonOptions>>((options, json) => options.JsonSerializerOptions = json.Value.SerializerOptions);

        return services;
    }

    private static void Configure(IdempotencyOptions options)
    {
        // A repeat after a 2xx answer replays it; the default answers every repeat with 409.
        options.ConflictHandling = IdempotentConflictHandling.CachedResult;
        // The upload's 300-second request timeout plus 30 seconds: the hold never ends while an upload still runs.
        options.InFlightReservationTimeout = TimeSpan.FromSeconds(330);
        options.Expiration = TimeSpan.FromHours(4);
        options.KeyScopeResolver = IdempotencyKeyScope.Resolve;
    }

    #endregion
}
