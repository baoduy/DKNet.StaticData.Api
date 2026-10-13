using DKNet.StaticData.App.Tests.Files.Support;
using DKNet.StaticData.App.TestSupport;
using DKNet.StaticData.Infra.Contexts;
using Microsoft.EntityFrameworkCore;

namespace DKNet.StaticData.App.Tests.Integration.Migrations;

/// <summary>
/// Brief DRK-2239 §3 rows 3-6: the service's model holds no change its own migrations do not, on either database — every
/// length, index, collation and key of the mappings is in each database's migrations, so the schema the migrations
/// build is the one the mappings describe.
/// </summary>
public sealed class ModelMatchesMigrationsTests(FilesHost hosts) : IClassFixture<FilesHost>
{
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task TheModelHoldsNoChangeTheMigrationsDoNot(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        using var scope = api.Host.Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<CoreDbContext>().Database.HasPendingModelChanges().ShouldBeFalse();
    }
}
