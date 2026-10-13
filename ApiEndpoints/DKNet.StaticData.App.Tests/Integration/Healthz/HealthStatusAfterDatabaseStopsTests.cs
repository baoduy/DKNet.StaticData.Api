using DKNet.StaticData.App.TestSupport;

namespace DKNet.StaticData.App.Tests.Integration.Healthz;

/// <summary>
/// DRK-2198 §6a row D9, after a healthy probe: the probe's pooled connection must not keep the status route
/// Healthy once the database server stops — the check has to reach the server on every call.
/// </summary>
public class HealthStatusAfterDatabaseStopsTests
{
    [Theory]
    [InlineData(TestDatabase.Postgres, "Postgres")]
    [InlineData(TestDatabase.SqlServer, "SqlServer")]
    public async Task HealthStatus_DatabaseStopsAfterAHealthyProbe_IsUnhealthy(TestDatabase database, string choice)
    {
        await using var server = await TestDatabaseServer.StartAsync(database);
        var (_, connectionString) = await server.CreateEmptyDatabaseAsync();
        var (host, startupError) = await DatabaseSettingApiFactory.StartAsync(
            choice, connectionString, runDbMigrationWhenAppStart: true);
        startupError.ShouldBeNull();
        await using var _ = host!;
        using var client = host!.CreateClient();
        using (var healthy = await client.GetAsync("/healthz"))
        {
            healthy.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await healthy.Content.ReadAsStringAsync()).ShouldBe("""{"status":"Healthy"}""");
        }

        await server.StopAsync();
        using var response = await client.GetAsync("/healthz");

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadAsStringAsync()).ShouldBe("""{"status":"Unhealthy"}""");
    }
}
