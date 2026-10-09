using System.Diagnostics.CodeAnalysis;
using DKNet.EfCore.Extensions.Extensions;
using DKNet.EfCore.Hooks;
using DKNet.EfCore.Specifications;
using DKNet.StaticData.Infra.Contexts;
using DKNet.StaticData.Infra.Services;

namespace DKNet.StaticData.Infra.Extensions;

/// <summary>
/// Registers infrastructure-layer services, repository/service implementations,
/// and database context configuration for the application.
/// </summary>
[ExcludeFromCodeCoverage]
public static class InfraSetup
{
    #region Methods

    /// <summary>
    /// Adds infrastructure dependencies, including infra services,
    /// domain event publishing, and the EF Core <see cref="CoreDbContext"/> setup.
    /// </summary>
    /// <param name="service">The service collection used to register dependencies.</param>
    /// <param name="useDatabase">
    /// The chosen database's provider setup, given the options builder and the <c>AppDb</c> connection string.
    /// </param>
    /// <returns>The same <see cref="IServiceCollection"/> instance for chaining.</returns>
    public static IServiceCollection AddInfraServices(
        this IServiceCollection service,
        Action<DbContextOptionsBuilder, string> useDatabase)
    {
        service
            .AddSpecRepo<CoreDbContext>()
            .AddEventPublisher<CoreDbContext, EventPublisher>()
            .AddDbContextWithHook<CoreDbContext>((sp, builder) =>
            {
                var config = sp.GetRequiredService<IConfiguration>();
                var conn = config.GetConnectionString(SharedConsts.DbConnectionString)!;

                useDatabase(builder, conn);
                builder.UseAutoConfigModel([typeof(CoreDbContext).Assembly, typeof(DomainEntity).Assembly])
                    .UseAutoDataSeeding([typeof(InfraSetup).Assembly]);
            });

        return service;
    }

    #endregion
}