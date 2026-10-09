using System.Diagnostics.CodeAnalysis;
using DKNet.EfCore.Extensions.Extensions;
using DKNet.StaticData.Infra.Contexts;

namespace DKNet.StaticData.Infra.Extensions;

/// <summary>
///
/// </summary>
[ExcludeFromCodeCoverage]
public static class InfraMigration
{
    #region Methods

    /// <summary>
    /// Migrates the database to the latest version. This method should be called during application startup to ensure that the database schema is up to date before the application starts handling requests.
    /// </summary>
    /// <param name="connectionString">The <c>AppDb</c> connection string.</param>
    /// <param name="useDatabase">The chosen database's provider setup, which brings that database's migrations.</param>
    public static async Task MigrateDb(string connectionString, Action<DbContextOptionsBuilder, string> useDatabase)
    {
        //Db migration
        var options = new DbContextOptionsBuilder<CoreDbContext>()
            .UseAutoConfigModel([typeof(CoreDbContext).Assembly, typeof(DomainEntity).Assembly]);
        useDatabase(options, connectionString);
        options.UseAutoDataSeeding([typeof(InfraSetup).Assembly]);

        await using var db = new CoreDbContext(options.Options);

        // Seeding runs as part of MigrateAsync via UseAutoDataSeeding above.
        await db.Database.MigrateAsync();
    }

    #endregion
}