using DKNet.StaticData.App.TestSupport;

namespace DKNet.StaticData.App.Tests.Integration.Healthz;

/// <summary>
/// DRK-2198 §6a row D8, token checking off: the health detail route answers without a token whether token checking
/// is on (EmptyService.feature) or off (here), on a real database of each kind.
/// </summary>
public class HealthDetailTokenCheckingOffTests
{
    [Theory]
    [InlineData(TestDatabase.Postgres, "Postgres")]
    [InlineData(TestDatabase.SqlServer, "SqlServer")]
    public async Task HealthDetail_TokenCheckingOff_ReportsOnlyTheDatabaseCheckWithoutAToken(
        TestDatabase database, string choice)
    {
        var server = await TestDatabaseServer.SharedAsync(database);
        var (_, connectionString) = await server.CreateEmptyDatabaseAsync();
        var (host, startupError) = await DatabaseSettingApiFactory.StartAsync(
            choice, connectionString, runDbMigrationWhenAppStart: true, requireAuthorization: false);
        startupError.ShouldBeNull();
        await using var _ = host!;
        using var client = host!.CreateClient();

        using var response = await client.GetAsync("/healthz/detail");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var report = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var entries = report.RootElement.GetProperty("entries");
        entries.EnumerateObject().Select(p => p.Name).ShouldBe(["CoreDbContext"]);
        entries.GetProperty("CoreDbContext").GetProperty("status").GetString().ShouldBe("Healthy");
    }
}
