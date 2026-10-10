using System.Collections.Concurrent;
using System.Data.Common;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using Microsoft.Data.SqlClient;
using Npgsql;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;

namespace DKNet.StaticData.App.TestSupport;

/// <summary>The 2 databases the service runs on.</summary>
public enum TestDatabase
{
    Postgres,
    SqlServer
}

/// <summary>
/// A real database server in a container, for the tests that run the service against a live Postgres or SQL Server
/// (spec DRK-2198 §3: every database test runs on both). <see cref="SharedAsync"/> starts one server per database for
/// the whole test run; a test that stops its server takes its own from <see cref="StartAsync"/> instead.
/// </summary>
public sealed class TestDatabaseServer : IAsyncDisposable
{
    private const string PostgresImage = "postgres:16-alpine";

    /// <summary>
    /// Environment variable that overrides the SQL Server image. The default runs on amd64 (CI); an arm64 runtime,
    /// where SQL Server crashes under emulation, sets <c>mcr.microsoft.com/azure-sql-edge</c> (DRK-2198 Q1).
    /// </summary>
    public const string SqlServerImageVariable = "TEST_SQLSERVER_IMAGE";

    private const string DefaultSqlServerImage = "mcr.microsoft.com/mssql/server:2022-latest";

    private static readonly ConcurrentDictionary<TestDatabase, Lazy<Task<TestDatabaseServer>>> Shared = new();

    private readonly IDatabaseContainer _container;

    private TestDatabaseServer(TestDatabase database, IDatabaseContainer container)
    {
        Database = database;
        _container = container;
    }

    public TestDatabase Database { get; }

    private static string SqlServerImage =>
        Environment.GetEnvironmentVariable(SqlServerImageVariable) is { Length: > 0 } image ? image : DefaultSqlServerImage;

    /// <summary>The server every test of this run shares. A server that failed to start fails every caller.</summary>
    public static Task<TestDatabaseServer> SharedAsync(TestDatabase database) =>
        Shared.GetOrAdd(database, d => new Lazy<Task<TestDatabaseServer>>(() => StartAsync(d))).Value;

    /// <summary>Starts a server the caller owns and disposes.</summary>
    public static async Task<TestDatabaseServer> StartAsync(TestDatabase database)
    {
        IDatabaseContainer container = database == TestDatabase.Postgres
            ? new PostgreSqlBuilder(PostgresImage).Build()
            // SQL Edge ships no sqlcmd, which the builder's own readiness check runs; a login works on both images.
            : new MsSqlBuilder(SqlServerImage)
                .WithWaitStrategy(Wait.ForUnixContainer().AddCustomWaitStrategy(new SqlServerLoginSucceeds()))
                .Build();
        await container.StartAsync();
        return new TestDatabaseServer(database, container);
    }

    /// <summary>Creates a new, empty database on this server and returns its name and connection string.</summary>
    public async Task<(string Name, string ConnectionString)> CreateEmptyDatabaseAsync()
    {
        var name = $"svc_{Guid.NewGuid():N}";
        var server = _container.GetConnectionString();
        await ExecuteAsync(server, $"CREATE DATABASE {name}");

        return (name, Database == TestDatabase.Postgres
            ? new NpgsqlConnectionStringBuilder(server) { Database = name }.ConnectionString
            : new SqlConnectionStringBuilder(server) { InitialCatalog = name }.ConnectionString);
    }

    /// <summary>Stops the server; every later connection to it fails.</summary>
    public Task StopAsync() => _container.StopAsync();

    /// <summary>Every user table of the database, as <c>schema.table</c>.</summary>
    public Task<IReadOnlyList<string>> ListTablesAsync(string connectionString) =>
        QueryNamesAsync(connectionString, Database == TestDatabase.Postgres
            ? "SELECT table_schema || '.' || table_name FROM information_schema.tables " +
              "WHERE table_schema NOT IN ('pg_catalog', 'information_schema')"
            : "SELECT s.name + '.' + t.name FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id");

    /// <summary>Every sequence of the database, as <c>schema.sequence</c>.</summary>
    public Task<IReadOnlyList<string>> ListSequencesAsync(string connectionString) =>
        QueryNamesAsync(connectionString, Database == TestDatabase.Postgres
            ? "SELECT sequence_schema || '.' || sequence_name FROM information_schema.sequences"
            : "SELECT s.name + '.' + q.name FROM sys.sequences q JOIN sys.schemas s ON s.schema_id = q.schema_id");

    public ValueTask DisposeAsync() => _container.DisposeAsync();

    private sealed class SqlServerLoginSucceeds : IWaitUntil
    {
        public async Task<bool> UntilAsync(IContainer container)
        {
            try
            {
                await using var connection = new SqlConnection(((MsSqlContainer)container).GetConnectionString());
                await connection.OpenAsync();
                return true;
            }
            catch (SqlException)
            {
                return false;
            }
        }
    }

    private DbConnection Connect(string connectionString) => Database == TestDatabase.Postgres
        ? new NpgsqlConnection(connectionString)
        : new SqlConnection(connectionString);

    /// <summary>
    /// Runs <paramref name="sql"/> on the database, with each <c>(name, value)</c> pair bound as a parameter the SQL
    /// names as <c>@name</c>. Returns the number of rows it changed.
    /// </summary>
    public async Task<int> ExecuteAsync(string connectionString, string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = Connect(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }

        return await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Deletes every row of every table outside the <c>migrate</c> schema, where the service and the idempotency store
    /// keep their migration history, so a host on this database starts its next test from empty tables without
    /// migrating again.
    /// </summary>
    public async Task DeleteAllRowsAsync(string connectionString)
    {
        foreach (var table in await ListTablesAsync(connectionString))
        {
            var (schema, name) = (table[..table.IndexOf('.', StringComparison.Ordinal)], table[(table.IndexOf('.', StringComparison.Ordinal) + 1)..]);
            if (schema == "migrate")
            {
                continue;
            }

            await ExecuteAsync(connectionString, $"DELETE FROM {Quote(schema)}.{Quote(name)}");
        }
    }

    /// <summary>Quotes an identifier the way this server's SQL needs.</summary>
    public string Quote(string identifier) => Database == TestDatabase.Postgres ? $"\"{identifier}\"" : $"[{identifier}]";

    /// <summary>The first column of every row <paramref name="sql"/> returns, as text.</summary>
    public Task<IReadOnlyList<string>> QueryTextsAsync(string connectionString, string sql) =>
        QueryNamesAsync(connectionString, sql);

    private async Task<IReadOnlyList<string>> QueryNamesAsync(string connectionString, string sql)
    {
        await using var connection = Connect(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }
}
