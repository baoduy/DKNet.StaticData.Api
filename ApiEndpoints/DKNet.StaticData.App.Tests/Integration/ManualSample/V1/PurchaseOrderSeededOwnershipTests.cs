using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using DKNet.StaticData.App.TestSupport;
using DKNet.StaticData.AppServices.ManualSample.V1;
using DKNet.StaticData.Infra.Contexts;
using DKNet.StaticData.Infra.Extensions;
using DKNet.StaticData.Share;
using Testcontainers.PostgreSql;

namespace DKNet.StaticData.App.Tests.Integration.ManualSample.V1;

/// <summary>
/// DRK-1901 acceptance tests: the three <c>PurchaseOrderStaticData</c> rows are owned by
/// <see cref="SharedConsts.SystemAccount"/> on BOTH context paths that run <c>UseAutoDataSeeding</c> — the
/// provider-less <see cref="InfraMigration.MigrateDb"/> context the app's migration job builds, and the
/// <c>InfraSetup.AddInfraServices</c> context resolved from the app's own container, where the data-owner
/// provider is registered but no request is in flight. Seeded rows must be visible to the
/// <see cref="SharedConsts.SystemAccount"/> caller and to no other subject. Runs against a real, ephemeral
/// Postgres container, because no InMemory fixture in this repo wires <c>UseAutoDataSeeding</c>.
/// </summary>
/// <remarks>
/// See <c>AuthOnApiFixture</c>'s remarks for why <c>RequireAuthorization</c> is flipped through an environment
/// variable set before the host builds, and why that is safe in this assembly.
/// </remarks>
public sealed class PurchaseOrderSeededOwnershipTests : IAsyncLifetime
{
    private const string RequireAuthorizationEnvKey = "FeatureManagement__RequireAuthorization";
    private const string EnableDemoAuthenticationEnvKey = "FeatureManagement__EnableDemoAuthentication";
    private const string OtherSubject = "opaque-subject-b";

    private static readonly Guid AcmeId = new("6E6F4D3C-1B7E-4C7A-9F1D-8A2B5C6D7E01");
    private static readonly Guid GlobexId = new("6E6F4D3C-1B7E-4C7A-9F1D-8A2B5C6D7E02");
    private static readonly Guid InitechId = new("6E6F4D3C-1B7E-4C7A-9F1D-8A2B5C6D7E03");

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private WebApplicationFactory<DKNet.StaticData.Api.Program>? _factory;

    #region Methods

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        Environment.SetEnvironmentVariable(RequireAuthorizationEnvKey, "true");
        Environment.SetEnvironmentVariable(EnableDemoAuthenticationEnvKey, "false");

        _factory = new WebApplicationFactory<DKNet.StaticData.Api.Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:AppDb"] = _postgres.GetConnectionString(),
                ["FeatureManagement:RunDbMigrationWhenAppStart"] = "false", // each test migrates through its own path
                ["FeatureManagement:EnableSwagger"] = "false",
                ["FeatureManagement:EnableAzureAppConfig"] = "false"
            }));
            builder.ConfigureServices(MultiSubjectAuthHandler.Register);
        });
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        Environment.SetEnvironmentVariable(RequireAuthorizationEnvKey, null);
        Environment.SetEnvironmentVariable(EnableDemoAuthenticationEnvKey, null);
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task SeededOrders_MigratedThroughInfraMigration_AreVisibleToSystemAccountOnly()
    {
        await InfraMigration.MigrateDb(_postgres.GetConnectionString());

        await AssertSeededOrdersVisibleToSystemAccountOnlyAsync();
    }

    [Fact]
    public async Task SeededOrders_MigratedThroughTheAppsDiContext_AreVisibleToSystemAccountOnly()
    {
        using (var scope = _factory!.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
            await dbContext.Database.MigrateAsync();
        }

        await AssertSeededOrdersVisibleToSystemAccountOnlyAsync();
    }

    private async Task AssertSeededOrdersVisibleToSystemAccountOnlyAsync()
    {
        var client = _factory!.CreateClient();

        var systemList = await ListAsAsync(client, SharedConsts.SystemAccount);
        systemList.ShouldContain(o => o.Id == AcmeId && o.CustomerName == "Acme Pte Ltd");
        systemList.ShouldContain(o => o.Id == GlobexId && o.CustomerName == "Globex Corporation");
        systemList.ShouldContain(o => o.Id == InitechId && o.CustomerName == "Initech LLC");

        using (var systemRead = await SendAsAsync(client, $"/v1/purchase-orders/{AcmeId}", SharedConsts.SystemAccount))
        {
            systemRead.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var otherList = await ListAsAsync(client, OtherSubject);
        otherList.ShouldNotContain(o => o.Id == AcmeId);
        otherList.ShouldNotContain(o => o.Id == GlobexId);
        otherList.ShouldNotContain(o => o.Id == InitechId);

        using var otherRead = await SendAsAsync(client, $"/v1/purchase-orders/{AcmeId}", OtherSubject);
        otherRead.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static async Task<List<PurchaseOrderDto>> ListAsAsync(HttpClient client, string subject)
    {
        using var response = await SendAsAsync(client, "/v1/purchase-orders?pageIndex=1&pageSize=20", subject);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<List<PurchaseOrderDto>>(SharedConsts.JsonSerializerOptions))!;
    }

    private static async Task<HttpResponseMessage> SendAsAsync(HttpClient client, string url, string subject)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add(MultiSubjectAuthHandler.SubjectHeaderName, subject);
        return await client.SendAsync(request);
    }

    #endregion
}
