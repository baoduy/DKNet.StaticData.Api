using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DKNet.StaticData.App.TestSupport;

/// <summary>
/// A host composed by the API's own start-up code with nothing about the database substituted — unlike
/// <see cref="TestApiFactoryBase"/>, which swaps <c>CoreDbContext</c> for EF Core InMemory — so the provider,
/// migrations and health checks seen here are the ones a deployment gets. Mirrors DKNet.Accounts.Api's
/// <c>DatabaseSettingApiFactory</c>: the settings arrive as environment variables, the one input <c>Program.cs</c>
/// reads before the host is built, and are restored once the host is built. Nothing is removed from the host
/// (hosted services included), so a start-up dependency on anything but the database still shows.
/// Safe only while no other host builds at the same time: both test assemblies run their tests one at a time.
/// </summary>
public sealed class DatabaseSettingApiFactory : WebApplicationFactory<DKNet.StaticData.Api.Program>
{
    private DatabaseSettingApiFactory()
    {
    }

    /// <summary>
    /// Builds and starts the host. <paramref name="choice"/> null leaves the database choice absent. Token checking
    /// is on by default, as in <c>appsettings.json</c>; the demonstration sign-in is always off. Returns the started
    /// host, or the exception the API's start-up threw (the factory is then already disposed).
    /// </summary>
    public static async Task<(DatabaseSettingApiFactory? Host, Exception? StartupError)> StartAsync(
        string? choice,
        string connectionString,
        bool runDbMigrationWhenAppStart = false,
        bool requireAuthorization = true)
    {
        var variables = new Dictionary<string, string?>
        {
            ["Database__Provider"] = choice,
            ["ConnectionStrings__AppDb"] = connectionString,
            ["ConnectionStrings__Redis"] = null,
            ["ConnectionStrings__AzureBus"] = null,
            ["FeatureManagement__RunDbMigrationWhenAppStart"] = runDbMigrationWhenAppStart ? "true" : "false",
            ["FeatureManagement__RequireAuthorization"] = requireAuthorization ? "true" : "false",
            ["FeatureManagement__EnableDemoAuthentication"] = "false"
        };
        var previous = variables.Keys.ToDictionary(k => k, Environment.GetEnvironmentVariable);

        var factory = new DatabaseSettingApiFactory();
        try
        {
            foreach (var (name, value) in variables)
            {
                Environment.SetEnvironmentVariable(name, value);
            }

            _ = factory.Services;
            return (factory, null);
        }
        catch (Exception ex)
        {
            await factory.DisposeAsync();
            return (null, ex);
        }
        finally
        {
            foreach (var (name, value) in previous)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Testing");
}
