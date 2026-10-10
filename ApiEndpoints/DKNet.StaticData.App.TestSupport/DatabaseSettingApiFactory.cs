using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DKNet.StaticData.App.TestSupport;

/// <summary>
/// A host composed by the API's own start-up code with nothing about the database substituted — unlike
/// <see cref="TestApiFactoryBase"/>, which swaps <c>CoreDbContext</c> for EF Core InMemory — so the provider,
/// migrations and health checks seen here are the ones a deployment gets. Mirrors DKNet.Accounts.Api's
/// <c>DatabaseSettingApiFactory</c>: the settings arrive through <see cref="TestHost"/>. Nothing is removed from the
/// host (hosted services included), so a start-up dependency on anything but the database still shows.
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
    public static Task<(DatabaseSettingApiFactory? Host, Exception? StartupError)> StartAsync(
        string? choice,
        string connectionString,
        bool runDbMigrationWhenAppStart = false,
        bool requireAuthorization = true) =>
        TestHost.StartAsync(
            new DatabaseSettingApiFactory(),
            TestHost.DatabaseSettings(choice, connectionString, runDbMigrationWhenAppStart, requireAuthorization));

    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Testing");
}
