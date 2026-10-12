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
                // 3 retries (waits of about 0 + 1 + 3 s): a database that cannot be reached fails well inside the 30 s
                // request timeout and answers 503. The default 6 retries waited about 56 s.
                .EnableRetryOnFailure(maxRetryCount: 3)
                .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));
    }
}
