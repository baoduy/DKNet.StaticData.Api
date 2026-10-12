using DKNet.StaticData.Domains.Share;
using DKNet.StaticData.Infra.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace DKNet.StaticData.Infra.Postgres;

/// <summary>
/// PostgreSQL setup for <c>CoreDbContext</c>: the Npgsql provider and this project's own migrations.
/// </summary>
public static class PostgresSetup
{
    /// <summary>
    /// The longest one connection attempt may wait, in seconds. With 3 retries the worst case is 4 attempts of 4 s plus
    /// waits of about 0 + 1 + 3 s, about 20 s: a host that drops packets still answers 503 inside the 30 s request
    /// timeout. Npgsql's default is 15 s, which took about 64 s. Npgsql's <c>Timeout</c> also bounds the wait for a free
    /// pooled connection, so a pool exhausted for over 4 s fails the call too.
    /// </summary>
    internal const int MaxConnectTimeoutSeconds = 4;

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
            BoundConnectTimeout(connectionString),
            o => o
                .MinBatchSize(1)
                .MaxBatchSize(100)
                .MigrationsHistoryTable(nameof(CoreDbContext), DomainSchemas.Migration)
                .MigrationsAssembly(typeof(PostgresSetup).Assembly)
                // 3 retries (waits of about 0 + 1 + 3 s): a database that refuses connections fails well inside the
                // 30 s request timeout and answers 503. The default 6 retries waited about 56 s.
                .EnableRetryOnFailure(maxRetryCount: 3)
                .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));
    }

    /// <summary>
    /// Caps the connection string's <c>Timeout</c> at <see cref="MaxConnectTimeoutSeconds"/>. A shorter timeout is kept;
    /// 0, which Npgsql reads as "wait forever", is capped too.
    /// </summary>
    internal static string BoundConnectTimeout(string connectionString)
    {
        var settings = new NpgsqlConnectionStringBuilder(connectionString);
        if (settings.Timeout is <= 0 or > MaxConnectTimeoutSeconds)
        {
            settings.Timeout = MaxConnectTimeoutSeconds;
        }

        return settings.ConnectionString;
    }
}
