using System.Diagnostics.CodeAnalysis;
using DKNet.StaticData.Infra.Extensions;

namespace DKNet.StaticData.Infra.Contexts;

/// <summary>
/// The design-time service provider each database project's <c>DbContextFactory</c> resolves
/// <see cref="CoreDbContext"/> from, so <c>dotnet ef</c> builds the context the way the runtime does.
/// </summary>
[ExcludeFromCodeCoverage]
internal static class DesignTimeServices
{
    #region Methods

    /// <summary>
    /// <c>UseAutoDataSeeding</c>'s post-migration hook publishes domain events through
    /// <see cref="DKNet.StaticData.Infra.Services.EventPublisher"/>, which needs an <c>IMessageBus</c> to activate —
    /// without one, <c>dotnet ef database update</c> throws while resolving the seeding hook. Calls
    /// <see cref="ServiceBusSetup.AddMemoryBus"/> to register the same in-memory-only bus the runtime path uses,
    /// still without <c>AddSlimBusEfCoreInterceptor</c>. The scanned assembly here is
    /// <c>typeof(InfraSetup).Assembly</c> rather than the app assembly the runtime path passes —
    /// <c>DKNet.StaticData.Infra</c> cannot reference <c>DKNet.StaticData.Api</c>, so the app assembly is unreachable
    /// at design time. Today that's harmless: nothing is seeded. A future <c>DataSeedingConfiguration&lt;T&gt;</c>
    /// over an event-raising entity would need a declared producer for its event to be handled here.
    /// </summary>
    /// <param name="useDatabase">The database project's provider setup.</param>
    /// <param name="connectionString">A placeholder connection string; design time never connects.</param>
    internal static IServiceProvider Build(Action<DbContextOptionsBuilder, string> useDatabase, string connectionString)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    [$"ConnectionStrings:{SharedConsts.DbConnectionString}"] = connectionString
                })
            .Build();

        return new ServiceCollection()
            .AddSingleton<IConfiguration>(config)
            .AddInfraServices(useDatabase)
            .AddSlimMessageBus(mbb => mbb.AddJsonSerializer().AddMemoryBus(typeof(InfraSetup).Assembly))
            .AddLogging()
            .BuildServiceProvider();
    }

    #endregion
}
