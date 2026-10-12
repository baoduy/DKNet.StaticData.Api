using DKNet.StaticData.Infra.Contexts;
using DKNet.StaticData.Infra.Postgres;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.Internal;

namespace DKNet.StaticData.App.Tests.Unit.Infra;

/// <summary>
/// Brief DRK-2240 §3 row 6: every Postgres connection attempt waits at most 4 seconds, so a host that drops packets
/// fails all 4 attempts (3 retries) inside the 30-second request timeout and answers 503, never 504.
/// </summary>
public class PostgresSetupTests
{
    private const string Server = "Host=10.255.255.1;Port=5432;Database=staticdata;Username=svc;Password=secret";

    [Theory]
    [InlineData("", 4)]
    [InlineData(";Timeout=0", 4)]
    [InlineData(";Timeout=1", 1)]
    [InlineData(";Timeout=4", 4)]
    [InlineData(";Timeout=5", 4)]
    [InlineData(";Timeout=15", 4)]
    public void EachConnectionAttemptWaitsAtMost4Seconds(string timeout, int expectedSeconds)
    {
        var options = new DbContextOptionsBuilder<CoreDbContext>().UsePostgres(Server + timeout).Options;

        var connectionString = options.FindExtension<NpgsqlOptionsExtension>().ShouldNotBeNull().ConnectionString;

        var settings = new NpgsqlConnectionStringBuilder(connectionString);
        settings.Timeout.ShouldBe(expectedSeconds);
        (settings.Host, settings.Port, settings.Database, settings.Username, settings.Password)
            .ShouldBe(("10.255.255.1", 5432, "staticdata", "svc", "secret"));
    }
}
