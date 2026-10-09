using DKNet.StaticData.Domains.Share;
using DKNet.StaticData.Infra.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DKNet.StaticData.Infra.Postgres;

/// <summary>
/// PostgreSQL setup for <c>CoreDbContext</c>: the Npgsql provider and this project's own migrations.
/// </summary>
public static class PostgresSetup
{
    /// <summary>
    /// Configures the Npgsql provider, the migrations history table and this assembly's migrations.
    /// </summary>
    /// <param name="builder">The options builder to configure.</param>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <returns>The configured <see cref="DbContextOptionsBuilder"/>.</returns>
    public static DbContextOptionsBuilder UsePostgres(this DbContextOptionsBuilder builder, string connectionString)
    {
        builder.ConfigureWarnings(warnings => warnings.Log(RelationalEventId.PendingModelChangesWarning));

        return builder.UseNpgsql(
            connectionString,
            o => o
                .MinBatchSize(1)
                .MaxBatchSize(100)
                .MigrationsHistoryTable(nameof(CoreDbContext), DomainSchemas.Migration)
                .MigrationsAssembly(typeof(PostgresSetup).Assembly)
                .EnableRetryOnFailure()
                .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));
    }
}
