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

    /// <summary>
    /// Every migrated table as <c>schema.table</c>, with the database's own default schema (Postgres <c>public</c>, SQL
    /// Server <c>dbo</c>) written as <c>default</c>: the DKNet idempotency store keeps its table there on each database.
    /// </summary>
    private static async Task<string[]> MigratedTablesAsync(TestDatabase database, string choice)
    {
        var defaultSchema = database == TestDatabase.Postgres ? "public." : "dbo.";
        var server = await TestDatabaseServer.SharedAsync(database);
        var (_, connectionString) = await server.CreateEmptyDatabaseAsync();
        var (host, startupError) = await DatabaseSettingApiFactory.StartAsync(
            choice, connectionString, runDbMigrationWhenAppStart: true);
        startupError.ShouldBeNull();
        await host!.DisposeAsync();

        return [.. (await server.ListTablesAsync(connectionString))
            .Select(t => t.StartsWith(defaultSchema, StringComparison.Ordinal) ? "default." + t[defaultSchema.Length..] : t)
            .Order(StringComparer.Ordinal)];
    }
}
