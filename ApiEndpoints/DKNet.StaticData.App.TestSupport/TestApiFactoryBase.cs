using DKNet.AspCore.Idempotency;
using DKNet.AspCore.Idempotency.Store;
using DKNet.EfCore.Hooks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DKNet.StaticData.Infra.Contexts;

namespace DKNet.StaticData.App.TestSupport;

/// <summary>
/// Shared host substitution for <c>WebApplicationFactory&lt;DKNet.StaticData.Api.Program&gt;</c> — swaps the real
/// DbContext for EF Core InMemory, the same substitution both the xUnit integration suite and the Reqnroll BDD
/// suite need. Suite-specific concerns (per-scenario feature overrides, IAsyncLifetime) belong in a subclass.
/// </summary>
public abstract class TestApiFactoryBase(string? dbName = null) : WebApplicationFactory<DKNet.StaticData.Api.Program>
{
    private const string AppDbVariable = "ConnectionStrings__AppDb";
    private const string InMemoryConnectionString = "UseInMemory";

    private readonly string _dbName = dbName ?? $"tests-{Guid.NewGuid():N}";

    /// <summary>Captures log lines written by the app during a scenario/test, for asserting on log output.</summary>
    public TestLogCapture LogCapture { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging => logging.AddProvider(LogCapture));
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(BuildFeatureOverrides()));
        builder.ConfigureServices(ConfigureTestServices);
    }

    /// <summary>
    /// <c>Program</c> registers the idempotency store with the <c>AppDb</c> connection string before the configuration
    /// added through the factory is merged (see <see cref="TestHost"/>), and the store refuses an empty one: the value
    /// arrives as an environment variable while the host builds, and is restored after.
    /// </summary>
    protected override IHost CreateHost(IHostBuilder builder)
    {
        var previous = Environment.GetEnvironmentVariable(AppDbVariable);
        Environment.SetEnvironmentVariable(AppDbVariable, InMemoryConnectionString);
        try
        {
            return base.CreateHost(builder);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AppDbVariable, previous);
        }
    }

    /// <summary>
    /// Base <c>FeatureManagement</c>/connection-string overrides both suites need. Override
    /// <see cref="AddFeatureOverrides" /> to extend rather than replacing this set.
    /// </summary>
    private Dictionary<string, string?> BuildFeatureOverrides()
    {
        var settings = new Dictionary<string, string?>
        {
            ["FeatureManagement:RunDbMigrationWhenAppStart"] = "false",
            ["FeatureManagement:EnableSwagger"] = "false",
            ["FeatureManagement:EnableAzureAppConfig"] = "false",
            ["ConnectionStrings:AppDb"] = InMemoryConnectionString
        };
        AddFeatureOverrides(settings);
        return settings;
    }

    /// <summary>Extension point for a subclass's additional configuration overrides.</summary>
    protected virtual void AddFeatureOverrides(IDictionary<string, string?> settings)
    {
    }

    /// <summary>
    /// Swaps the real DbContext for EF Core InMemory. Override to extend (call <c>base.ConfigureTestServices</c>
    /// first).
    /// </summary>
    protected virtual void ConfigureTestServices(IServiceCollection services)
    {
        services.RemoveAll<IDbContextOptionsConfiguration<CoreDbContext>>();
        services.RemoveAll<IConfigureOptions<DbContextOptions<CoreDbContext>>>();
        services.RemoveAll<IPostConfigureOptions<DbContextOptions<CoreDbContext>>>();
        services.RemoveAll<DbContextOptions<CoreDbContext>>();
        services.RemoveAll<CoreDbContext>();

        // AddDbContext (rather than AddDbContextWithHook) here would silently drop the DKNet events hook —
        // AddEvent-raised and [RaisesEvent]-declared domain events would never publish under this fixture.
        services.AddDbContextWithHook<CoreDbContext>((_, options) => options
            .UseInMemoryDatabase(_dbName)
            .UseAutoConfigModel([typeof(CoreDbContext).Assembly]));

        // The service keeps idempotency records in its SQL database, migrated by a hosted service at start; with no
        // SQL database here, the package's own in-memory store takes its place, and nothing migrates.
        services.RemoveAll<IIdempotencyKeyStore>();
        foreach (var migration in services
                     .Where(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType?.Name
                         .StartsWith("IdempotencyMigrationHostedService", StringComparison.Ordinal) == true)
                     .ToArray())
        {
            services.Remove(migration);
        }

        services.AddIdempotentKey();
    }

    public IServiceScope CreateScope() => Services.CreateScope();

    public async Task ResetDatabaseAsync()
    {
        using var scope = CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.EnsureCreatedAsync();
        LogCapture.Clear();
    }
}
