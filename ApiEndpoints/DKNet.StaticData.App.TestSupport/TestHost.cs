using Microsoft.AspNetCore.Mvc.Testing;

namespace DKNet.StaticData.App.TestSupport;

/// <summary>
/// Starts a <see cref="WebApplicationFactory{TEntryPoint}"/> host with settings that <c>Program.cs</c> reads before the
/// host is built — the database choice, the connection string and the feature flags. Those arrive as environment
/// variables, the one input read that early (configuration added through the factory is merged only at
/// <c>Build()</c>), and are restored once the host has started. Safe only while no other host builds at the same time:
/// both test assemblies run their tests one at a time.
/// </summary>
public static class TestHost
{
    /// <summary>
    /// The variables a host on a real database needs. <paramref name="choice"/> null leaves the database choice absent;
    /// the demonstration sign-in is always off.
    /// </summary>
    public static Dictionary<string, string?> DatabaseSettings(
        string? choice,
        string connectionString,
        bool runDbMigrationWhenAppStart,
        bool requireAuthorization) =>
        new()
        {
            ["Database__Provider"] = choice,
            ["ConnectionStrings__AppDb"] = connectionString,
            ["ConnectionStrings__Redis"] = null,
            ["ConnectionStrings__AzureBus"] = null,
            ["FeatureManagement__RunDbMigrationWhenAppStart"] = runDbMigrationWhenAppStart ? "true" : "false",
            ["FeatureManagement__RequireAuthorization"] = requireAuthorization ? "true" : "false",
            ["FeatureManagement__EnableDemoAuthentication"] = "false"
        };

    /// <summary>
    /// Builds and starts <paramref name="factory"/>'s host with <paramref name="variables"/> set. Returns the started
    /// host, or the exception the API's start-up threw (the factory is then already disposed).
    /// </summary>
    public static async Task<(TFactory? Host, Exception? StartupError)> StartAsync<TFactory>(
        TFactory factory,
        IReadOnlyDictionary<string, string?> variables)
        where TFactory : WebApplicationFactory<DKNet.StaticData.Api.Program>
    {
        var previous = variables.Keys.ToDictionary(k => k, Environment.GetEnvironmentVariable);
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
}
