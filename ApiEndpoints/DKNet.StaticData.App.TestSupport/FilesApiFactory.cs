using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using DKNet.StaticData.Infra.Contexts;

namespace DKNet.StaticData.App.TestSupport;

/// <summary>
/// A host of the file routes as a deployment runs them: the service's own start-up code on a real, migrated Postgres or
/// SQL Server database, token and role checks on, the service's own JWT bearer scheme trusting <see cref="TestTokens"/>,
/// and blob storage on a local folder (<see cref="BlobRoot"/>). Beyond the bearer scheme's trust, four seams are added
/// and nothing else is swapped:
/// <list type="bullet">
/// <item><see cref="LogCapture"/> records every log entry the host writes.</item>
/// <item><see cref="Blob"/> fails, holds or records blob storage calls (<see cref="ControllableBlobService"/>).</item>
/// <item><see cref="SaveFailure"/> makes saving the service's records fail.</item>
/// <item>An optional <see cref="TimeProvider"/> replaces the host's clock.</item>
/// </list>
/// </summary>
public sealed class FilesApiFactory : WebApplicationFactory<DKNet.StaticData.Api.Program>
{
    private readonly TimeProvider? _timeProvider;

    private FilesApiFactory(TestDatabase database, string connectionString, string blobRoot, TimeProvider? timeProvider)
    {
        Database = database;
        ConnectionString = connectionString;
        BlobRoot = blobRoot;
        _timeProvider = timeProvider;
    }

    public TestDatabase Database { get; }

    public string ConnectionString { get; }

    /// <summary>The local folder blob storage keeps the bytes in.</summary>
    public string BlobRoot { get; }

    public TestLogCapture LogCapture { get; } = new();

    public BlobStorageControl Blob { get; } = new();

    public FailingSaveInterceptor SaveFailure { get; } = new();

    /// <summary>
    /// Starts a host on <paramref name="connectionString"/>, migrating the database at start, with blob storage on the
    /// local folder <paramref name="blobRoot"/>. Throws when the service does not start.
    /// </summary>
    public static async Task<FilesApiFactory> StartAsync(
        TestDatabase database,
        string connectionString,
        string blobRoot,
        TimeProvider? timeProvider = null)
    {
        var variables = TestHost.DatabaseSettings(
            database == TestDatabase.Postgres ? "Postgres" : "SqlServer",
            connectionString,
            runDbMigrationWhenAppStart: true,
            requireAuthorization: true);
        variables["BlobStorage__Provider"] = "Local";
        variables["BlobStorage__LocalFolder__RootFolder"] = blobRoot;

        var (host, startupError) = await TestHost.StartAsync(
            new FilesApiFactory(database, connectionString, blobRoot, timeProvider), variables);
        return host ?? throw new InvalidOperationException("The service did not start.", startupError);
    }

    /// <summary>Clears every seam back to off and empties the log, for the next test on the same host.</summary>
    public void ResetSeams()
    {
        Blob.Reset();
        SaveFailure.Enabled = false;
        LogCapture.Clear();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging => logging.AddProvider(LogCapture));
        builder.ConfigureTestServices(services =>
        {
            TestTokens.TrustTestIssuer(services);
            ControllableBlobService.Wrap(services, Blob);
            services.ConfigureDbContext<CoreDbContext>(options => options.AddInterceptors(SaveFailure));
            if (_timeProvider is not null)
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton(_timeProvider);
            }
        });
    }
}
