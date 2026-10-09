using System.Diagnostics.CodeAnalysis;
using DKNet.StaticData.Infra.Contexts;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;

namespace DKNet.StaticData.Infra.MsSql;

/// <summary>Builds <see cref="CoreDbContext"/> on SQL Server for <c>dotnet ef</c> against this project.</summary>
[ExcludeFromCodeCoverage]
internal sealed class DbContextFactory : IDesignTimeDbContextFactory<CoreDbContext>
{
    #region Methods

    public CoreDbContext CreateDbContext(string[] args) =>
        DesignTimeServices.Build(
                (builder, connectionString) => builder.UseMsSql(connectionString),
                "Server=localhost;Database=SampleDb;User Id=sa;Password=unused;TrustServerCertificate=True")
            .GetRequiredService<CoreDbContext>();

    #endregion
}
