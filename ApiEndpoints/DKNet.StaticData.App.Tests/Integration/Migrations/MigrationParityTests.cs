using DKNet.StaticData.App.TestSupport;

namespace DKNet.StaticData.App.Tests.Integration.Migrations;

/// <summary>
/// DRK-2198 §6 R2, spec DRK-2206 "Must stay true" and spec DRK-2245 §5 "Both databases reach the same tables, keys and
/// indexes": both databases reach the same tables, primary keys and indexes
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

    /// <summary>Each column of the <c>settings</c> schema as <c>schema.table.column|type|nullable|max length</c>.</summary>
    private const string PostgresSettingsColumns =
        "SELECT table_schema || '.' || table_name || '.' || column_name || '|' || data_type || '|' || is_nullable || '|' || " +
        "COALESCE(character_maximum_length::text, '') FROM information_schema.columns WHERE table_schema = 'settings'";

    private const string SqlServerSettingsColumns =
        "SELECT TABLE_SCHEMA + '.' + TABLE_NAME + '.' + COLUMN_NAME + '|' + DATA_TYPE COLLATE DATABASE_DEFAULT + '|' + IS_NULLABLE COLLATE DATABASE_DEFAULT + '|' + " +
        "COALESCE(CAST(CHARACTER_MAXIMUM_LENGTH AS varchar(11)), '') FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = 'settings'";

    /// <summary>Each foreign key as <c>schema.table.column -&gt; schema.table.column|delete rule</c>.</summary>
    private const string PostgresForeignKeys =
        "SELECT sn.nspname || '.' || st.relname || '.' || sa.attname || ' -> ' || tn.nspname || '.' || tt.relname || '.' || " +
        "ta.attname || '|' || c.confdeltype::text FROM pg_constraint c " +
        "JOIN pg_class st ON st.oid = c.conrelid JOIN pg_namespace sn ON sn.oid = st.relnamespace " +
        "JOIN pg_class tt ON tt.oid = c.confrelid JOIN pg_namespace tn ON tn.oid = tt.relnamespace " +
        "JOIN pg_attribute sa ON sa.attrelid = c.conrelid AND sa.attnum = c.conkey[1] " +
        "JOIN pg_attribute ta ON ta.attrelid = c.confrelid AND ta.attnum = c.confkey[1] WHERE c.contype = 'f'";

    private const string SqlServerForeignKeys =
        "SELECT ss.name + '.' + st.name + '.' + sc.name + ' -> ' + ts.name + '.' + tt.name + '.' + tc.name + '|' + " +
        "fk.delete_referential_action_desc COLLATE DATABASE_DEFAULT FROM sys.foreign_keys fk " +
        "JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id " +
        "JOIN sys.tables st ON st.object_id = fkc.parent_object_id JOIN sys.schemas ss ON ss.schema_id = st.schema_id " +
        "JOIN sys.columns sc ON sc.object_id = fkc.parent_object_id AND sc.column_id = fkc.parent_column_id " +
        "JOIN sys.tables tt ON tt.object_id = fkc.referenced_object_id JOIN sys.schemas ts ON ts.schema_id = tt.schema_id " +
        "JOIN sys.columns tc ON tc.object_id = fkc.referenced_object_id AND tc.column_id = fkc.referenced_column_id";

    /// <summary>Spec DRK-2245 §3a: the columns of the setting and setting group tables, provider type names normalised.</summary>
    private static readonly string[] SettingsColumns =
    [
        "settings.SettingGroups.CreatedBy text(255) not null",
        "settings.SettingGroups.CreatedOn timestamp not null",
        "settings.SettingGroups.Description text(500) null",
        "settings.SettingGroups.Id uuid not null",
        "settings.SettingGroups.Name text(100) not null",
        "settings.SettingGroups.OwnedBy text(255) not null",
        "settings.SettingGroups.UpdatedBy text(255) null",
        "settings.SettingGroups.UpdatedOn timestamp null",
        "settings.SettingGroups.Version int not null",
        "settings.UiSettings.CreatedBy text(255) not null",
        "settings.UiSettings.CreatedOn timestamp not null",
        "settings.UiSettings.GroupId uuid null",
        "settings.UiSettings.Id uuid not null",
        "settings.UiSettings.OwnedBy text(255) not null",
        "settings.UiSettings.SchemaVersion int not null",
        "settings.UiSettings.SettingKey text(200) not null",
        "settings.UiSettings.SettingType text(50) not null",
        "settings.UiSettings.UpdatedBy text(255) null",
        "settings.UiSettings.UpdatedOn timestamp null",
        "settings.UiSettings.Value text not null",
        "settings.UiSettings.Version int not null"
    ];

    [Fact]
    public async Task Migrations_OnPostgresAndSqlServer_ReachTheSameTables()
    {
        var postgres = await MigratedSchemaAsync(TestDatabase.Postgres, "Postgres");
        var sqlServer = await MigratedSchemaAsync(TestDatabase.SqlServer, "SqlServer");

        postgres.Tables.ShouldNotBeEmpty("the Postgres migration created no table, not even its history");
        postgres.Indexes.ShouldContain("files.StoredFiles.PK_StoredFiles primary key");
        // spec DRK-2245 §5: the setting and setting group tables, their unique keys and the group link index
        postgres.Tables.ShouldContain("settings.UiSettings");
        postgres.Tables.ShouldContain("settings.SettingGroups");
        postgres.Indexes.ShouldContain("settings.UiSettings.PK_UiSettings primary key");
        postgres.Indexes.ShouldContain("settings.UiSettings.IX_UiSettings_OwnedBy_SettingKey unique");
        postgres.Indexes.ShouldContain("settings.UiSettings.IX_UiSettings_GroupId");
        postgres.Indexes.ShouldContain("settings.SettingGroups.PK_SettingGroups primary key");
        postgres.Indexes.ShouldContain("settings.SettingGroups.IX_SettingGroups_OwnedBy_Name unique");
        sqlServer.Tables.ShouldBe(postgres.Tables);
        sqlServer.Indexes.ShouldBe(postgres.Indexes);
        // spec DRK-2245 §5: the same columns and keys, the group link a foreign key with no cascade
        postgres.SettingsColumns.ShouldBe(SettingsColumns);
        sqlServer.SettingsColumns.ShouldBe(SettingsColumns);
        postgres.ForeignKeys.ShouldContain("settings.UiSettings.GroupId -> settings.SettingGroups.Id no cascade");
        sqlServer.ForeignKeys.ShouldBe(postgres.ForeignKeys);
    }

    /// <summary>
    /// Every migrated table as <c>schema.table</c>, and every index as <c>schema.table.index</c> followed by
    /// <c>primary key</c> or <c>unique</c> when it is one, with the database's own default schema (Postgres
    /// <c>public</c>, SQL Server <c>dbo</c>) written as <c>default</c>: the DKNet idempotency store keeps its table there
    /// on each database. Also each column of the <c>settings</c> schema as <c>schema.table.column type null|not null</c>,
    /// and every foreign key as <c>schema.table.column -&gt; schema.table.column delete-rule</c>, with the providers' type
    /// and delete rule names written the same way (<see cref="TypeClass"/>, <see cref="DeleteRule"/>).
    /// </summary>
    private static async Task<MigratedSchema> MigratedSchemaAsync(TestDatabase database, string choice)
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
        var postgres = database == TestDatabase.Postgres;
        var columns = await server.QueryTextsAsync(connectionString, postgres ? PostgresSettingsColumns : SqlServerSettingsColumns);
        var foreignKeys = await server.QueryTextsAsync(connectionString, postgres ? PostgresForeignKeys : SqlServerForeignKeys);
        return new MigratedSchema(
            Normalized(await server.ListTablesAsync(connectionString)),
            Normalized(indexes),
            [.. columns.Select(Column).Order(StringComparer.Ordinal)],
            Normalized(foreignKeys.Select(ForeignKey)));
    }

    /// <summary><c>schema.table.column|type|YES or NO|max length</c> as <c>schema.table.column type null|not null</c>.</summary>
    private static string Column(string row)
    {
        var parts = row.Split('|');
        return $"{parts[0]} {TypeClass(parts[1], parts[3])} {(parts[2] == "YES" ? "null" : "not null")}";
    }

    /// <summary>The provider's type name as one class both databases share; a text keeps its max length, unbounded none.</summary>
    private static string TypeClass(string type, string maxLength) => type switch
    {
        "uuid" or "uniqueidentifier" => "uuid",
        "integer" or "int" => "int",
        "timestamp with time zone" or "datetimeoffset" => "timestamp",
        "text" => "text",
        "character varying" or "nvarchar" => maxLength is "" or "-1" ? "text" : $"text({maxLength})",
        _ => type
    };

    /// <summary><c>… -&gt; …|rule</c> with the provider's delete rule written as both databases share it.</summary>
    private static string ForeignKey(string row)
    {
        var at = row.LastIndexOf('|');
        return $"{row[..at]} {DeleteRule(row[(at + 1)..])}";
    }

    /// <summary>Postgres <c>confdeltype</c> codes and SQL Server <c>delete_referential_action_desc</c> names.</summary>
    private static string DeleteRule(string rule) => rule switch
    {
        "a" or "r" or "NO_ACTION" => "no cascade",
        "c" or "CASCADE" => "cascade",
        "n" or "SET_NULL" => "set null",
        "d" or "SET_DEFAULT" => "set default",
        _ => rule
    };

    private sealed record MigratedSchema(string[] Tables, string[] Indexes, string[] SettingsColumns, string[] ForeignKeys);
}
