using DKNet.StaticData.Infra.MsSql;
using DKNet.StaticData.Infra.Postgres;

namespace DKNet.StaticData.Api.Configs;

/// <summary>
/// Chooses the database the service runs on from <see cref="SharedConsts.DatabaseProviderKey"/>.
/// </summary>
[ExcludeFromCodeCoverage]
internal static class DatabaseConfig
{
    #region Methods

    /// <summary>
    /// Reads the operator's database choice (<see cref="DatabaseProviders.Parse"/>): an unknown value throws
    /// <see cref="InvalidOperationException"/>. <c>Program.cs</c> calls it once the configuration sources are loaded,
    /// before the job dispatch and any app service registration.
    /// </summary>
    public static DatabaseProvider ResolveProvider(IConfiguration configuration) =>
        DatabaseProviders.Parse(configuration[SharedConsts.DatabaseProviderKey]);

    /// <summary>The chosen database's EF Core provider setup, which brings that database's own migrations.</summary>
    public static Action<DbContextOptionsBuilder, string> UseDatabase(DatabaseProvider provider) => provider switch
    {
        DatabaseProvider.Postgres => (builder, connectionString) => builder.UsePostgres(connectionString),
        DatabaseProvider.SqlServer => (builder, connectionString) => builder.UseMsSql(connectionString),
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unknown database.")
    };

    #endregion
}
