using DKNet.StaticData.App.TestSupport;

namespace DKNet.StaticData.App.Tests.Integration.Migrations;

/// <summary>
/// DRK-2198 §6 R2 and spec DRK-2206 "Must stay true": both databases reach the same tables, primary keys and indexes
/// (name and uniqueness) after their own migrations, each brought up to date by the service's start-up migration on a
/// real, empty database.
/// </summary>
public class MigrationParityTests
{
    private const string PostgresIndexes =
        "SELECT n.nspname || '.' || t.relname || '.' || i.relname || " +
        "CASE WHEN x.indisprimary THEN ' primary key' WHEN x.indisunique THEN ' unique' ELSE '' END " +
        "FROM pg_index x JOIN pg_class i ON i.oid = x.indexrelid JOIN pg_class t ON t.oid = x.indrelid " +
        "JOIN pg_namespace n ON n.oid = t.relnamespace " +
        "WHERE n.nspname NOT IN ('pg_catalog', 'information_schema') AND n.nspname NOT LIKE 'pg_toast%'";

    private const string SqlServerIndexes =
        "SELECT s.name + '.' + t.name + '.' + i.name + " +
        "CASE WHEN i.is_primary_key = 1 THEN ' primary key' WHEN i.is_unique = 1 THEN ' unique' ELSE '' END " +
        "FROM sys.indexes i JOIN sys.tables t ON t.object_id = i.object_id " +
        "JOIN sys.schemas s ON s.schema_id = t.schema_id WHERE i.type > 0";

    [Fact]
    public async Task Migrations_OnPostgresAndSqlServer_ReachTheSameTables()
    {
        var postgres = await MigratedSchemaAsync(TestDatabase.Postgres, "Postgres");
        var sqlServer = await MigratedSchemaAsync(TestDatabase.SqlServer, "SqlServer");

        postgres.Tables.ShouldNotBeEmpty("the Postgres migration created no table, not even its history");
        postgres.Indexes.ShouldContain("files.StoredFiles.PK_StoredFiles primary key");
        sqlServer.Tables.ShouldBe(postgres.Tables);
        sqlServer.Indexes.ShouldBe(postgres.Indexes);
    }

    /// <summary>
    /// Every migrated table as <c>schema.table</c>, and every index as <c>schema.table.index</c> followed by
    /// <c>primary key</c> or <c>unique</c> when it is one, with the database's own default schema (Postgres
    /// <c>public</c>, SQL Server <c>dbo</c>) written as <c>default</c>: the DKNet idempotency store keeps its table there
    /// on each database.
    /// </summary>
    private static async Task<(string[] Tables, string[] Indexes)> MigratedSchemaAsync(TestDatabase database, string choice)
    {
        var defaultSchema = database == TestDatabase.Postgres ? "public." : "dbo.";
        var server = await TestDatabaseServer.SharedAsync(database);
        var (_, connectionString) = await server.CreateEmptyDatabaseAsync();
        var (host, startupError) = await DatabaseSettingApiFactory.StartAsync(
            choice, connectionString, runDbMigrationWhenAppStart: true);
        startupError.ShouldBeNull();
        await host!.DisposeAsync();

        string[] Normalized(IEnumerable<string> names) =>
        [
            .. names
                .Select(n => n.StartsWith(defaultSchema, StringComparison.Ordinal) ? "default." + n[defaultSchema.Length..] : n)
                .Order(StringComparer.Ordinal)
        ];

        var indexes = await server.QueryTextsAsync(
            connectionString, database == TestDatabase.Postgres ? PostgresIndexes : SqlServerIndexes);
        return (Normalized(await server.ListTablesAsync(connectionString)), Normalized(indexes));
    }
}
