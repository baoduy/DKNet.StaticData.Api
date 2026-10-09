using DKNet.StaticData.App.TestSupport;

namespace DKNet.StaticData.App.Tests.Integration.Migrations;

/// <summary>
/// DRK-2198 §6 R2: both databases reach the same tables after their own migrations, each brought up to date by the
/// service's start-up migration on a real, empty database.
/// </summary>
public class MigrationParityTests
{
    [Fact]
    public async Task Migrations_OnPostgresAndSqlServer_ReachTheSameTables()
    {
        var postgres = await MigratedTablesAsync(TestDatabase.Postgres, "Postgres");
        var sqlServer = await MigratedTablesAsync(TestDatabase.SqlServer, "SqlServer");

        postgres.ShouldNotBeEmpty("the Postgres migration created no table, not even its history");
        sqlServer.ShouldBe(postgres);
    }

    private static async Task<string[]> MigratedTablesAsync(TestDatabase database, string choice)
    {
        var server = await TestDatabaseServer.SharedAsync(database);
        var (_, connectionString) = await server.CreateEmptyDatabaseAsync();
        var (host, startupError) = await DatabaseSettingApiFactory.StartAsync(
            choice, connectionString, runDbMigrationWhenAppStart: true);
        startupError.ShouldBeNull();
        await host!.DisposeAsync();

        return [.. (await server.ListTablesAsync(connectionString)).Order(StringComparer.Ordinal)];
    }
}
