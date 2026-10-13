using System.Diagnostics.CodeAnalysis;
using DKNet.StaticData.Infra.Contexts;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;

namespace DKNet.StaticData.Infra.Postgres;

/// <summary>Builds <see cref="CoreDbContext"/> on PostgreSQL for <c>dotnet ef</c> against this project.</summary>
[ExcludeFromCodeCoverage]
internal sealed class DbContextFactory : IDesignTimeDbContextFactory<CoreDbContext>
{
    #region Methods

    public CoreDbContext CreateDbContext(string[] args) => BuildServiceProvider(args).GetRequiredService<CoreDbContext>();

    /// <summary>
    /// The design-time service provider <see cref="CreateDbContext"/> resolves <see cref="CoreDbContext"/> from
    /// (<see cref="DesignTimeServices.Build"/>).
    /// </summary>
    internal static IServiceProvider BuildServiceProvider(string[] args) =>
        DesignTimeServices.Build(
            (builder, connectionString) => builder.UsePostgres(connectionString),
            "Host=localhost;Username=postgres;Password=postgres;Database=SampleDb");

    #endregion
}
