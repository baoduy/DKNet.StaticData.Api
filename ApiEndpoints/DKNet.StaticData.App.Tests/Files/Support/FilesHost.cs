using DKNet.StaticData.App.TestSupport;

namespace DKNet.StaticData.App.Tests.Files.Support;

/// <summary>
/// One test class's file hosts: a <see cref="FilesApiFactory"/> per database on a fresh database of the run's shared
/// server, started on first use and emptied before every later use, so each test starts from no files, no idempotency
/// records, an empty blob folder and every seam off. Scenarios that stop the database or replace the clock take a host
/// of their own instead.
/// </summary>
public sealed class FilesHost : IAsyncLifetime
{
    private readonly Dictionary<TestDatabase, FilesApi> _shared = new();
    private readonly List<FilesApiFactory> _hosts = [];
    private readonly List<TestDatabaseServer> _servers = [];
    private readonly List<string> _folders = [];

    /// <summary>The class's host on <paramref name="database"/>, emptied for this test.</summary>
    public async Task<FilesApi> OnAsync(TestDatabase database)
    {
        if (_shared.TryGetValue(database, out var api))
        {
            await api.ResetAsync();
            return api;
        }

        api = await OnFreshDatabaseAsync(database);
        _shared[database] = api;
        return api;
    }

    /// <summary>A host of its own on a new database of the shared server, optionally on <paramref name="timeProvider"/>.</summary>
    public async Task<FilesApi> OnFreshDatabaseAsync(TestDatabase database, TimeProvider? timeProvider = null)
    {
        var server = await TestDatabaseServer.SharedAsync(database);
        var (_, connectionString) = await server.CreateEmptyDatabaseAsync();
        return await StartAsync(server, connectionString, NewFolder(), timeProvider);
    }

    /// <summary>A host on a server of its own, which the test may stop.</summary>
    public async Task<FilesApi> OnOwnServerAsync(TestDatabase database, TimeProvider? timeProvider = null)
    {
        var server = await TestDatabaseServer.StartAsync(database);
        _servers.Add(server);
        var (_, connectionString) = await server.CreateEmptyDatabaseAsync();
        return await StartAsync(server, connectionString, NewFolder(), timeProvider);
    }

    /// <summary>"Another replica of the service": a second host on <paramref name="api"/>'s database and blob folder.</summary>
    public Task<FilesApi> ReplicaOfAsync(FilesApi api) =>
        StartAsync(api.Server, api.Host.ConnectionString, api.Host.BlobRoot, timeProvider: null);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var host in _hosts)
        {
            await host.DisposeAsync();
        }

        foreach (var server in _servers)
        {
            await server.DisposeAsync();
        }

        foreach (var folder in _folders.Where(Directory.Exists))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private async Task<FilesApi> StartAsync(
        TestDatabaseServer server,
        string connectionString,
        string blobRoot,
        TimeProvider? timeProvider)
    {
        var host = await FilesApiFactory.StartAsync(server.Database, connectionString, blobRoot, timeProvider);
        _hosts.Add(host);
        return new FilesApi(host, server);
    }

    private string NewFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "staticdata-files-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        _folders.Add(folder);
        return folder;
    }
}
