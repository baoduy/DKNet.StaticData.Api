using DKNet.EfCore.DataAuthorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using DKNet.StaticData.App.Tests.Integration.Support;
using DKNet.StaticData.Domains.Features.ManualSample.Entities;
using DKNet.StaticData.Infra.Contexts;

namespace DKNet.StaticData.App.Tests.Unit.ManualSample;

/// <summary>
/// DRK-1901: a purchase order is only saved when it has an owner. With a data-owner provider that resolves no
/// ownership key (an authenticated caller with no subject claim), a new order with no <c>OwnedBy</c> is refused
/// before EF Core saves it, while a seeded order that already carries its owner is saved unchanged.
/// </summary>
public sealed class PurchaseOrderOwnershipTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    #region Methods

    [Fact]
    public void HandBuiltContext_DoesNotShareTheApiHostsModel_SoTheHostKeepsItsOwnerFilter()
    {
        // The facts below build CoreDbContext by hand, outside the API host. EF Core caches one model per internal
        // service provider; sharing it would let a model built here first, before any host registered the owner
        // filter, be handed to the host, so the host would serve every caller's rows.
        using var handBuilt = new CoreDbContext(NewOptions());
        using var scope = fixture.CreateScope();
        var host = scope.ServiceProvider.GetRequiredService<CoreDbContext>();

        handBuilt.Model.ShouldNotBeSameAs(host.Model);
        host.Model.FindEntityType(typeof(PurchaseOrder))!.GetDeclaredQueryFilters().ShouldNotBeEmpty();
    }

    [Fact]
    public async Task NewOrder_WithNoResolvableOwnershipKey_IsRefused_AndNothingIsSaved()
    {
        var options = NewOptions();
        await using (var db = new CoreDbContext(options, [new NoKeyDataOwnerProvider()]))
        {
            db.Add(new PurchaseOrder("Orphan Traders", 1.23m, "alice"));

            await Should.ThrowAsync<OwnershipRequiredException>(() => db.SaveChangesAsync());
        }

        await using var reader = new CoreDbContext(options);
        (await reader.Set<PurchaseOrder>().IgnoreQueryFilters().CountAsync()).ShouldBe(0);
    }

    [Fact]
    public void NewOrder_WithNoResolvableOwnershipKey_IsRefused_OnSynchronousSave()
    {
        using var db = new CoreDbContext(NewOptions(), [new NoKeyDataOwnerProvider()]);
        db.Add(new PurchaseOrder("Orphan Traders", 1.23m, "alice"));

        Should.Throw<OwnershipRequiredException>(() => db.SaveChanges());
    }

    [Fact]
    public async Task SeededOrder_CarryingItsOwnOwner_IsSaved_AndKeepsThatOwner_WhenNoKeyResolves()
    {
        var options = NewOptions();
        var id = new Guid("6E6F4D3C-1B7E-4C7A-9F1D-8A2B5C6D7E01");
        await using (var db = new CoreDbContext(options, [new NoKeyDataOwnerProvider()]))
        {
            db.Add(new PurchaseOrder(id, "Acme Pte Ltd", 1250.00m, "System"));
            await db.SaveChangesAsync();
        }

        await using var reader = new CoreDbContext(options);
        var saved = await reader.Set<PurchaseOrder>().IgnoreQueryFilters().SingleAsync(p => p.Id == id);
        saved.OwnedBy.ShouldBe("System");
    }

    // Service-provider caching off: each hand-built context gets its own internal service provider, and so its own
    // model, instead of sharing the API host's. The InMemory store lives in that provider too, so the writer and
    // reader of one fact share it through an explicit database root.
    private static DbContextOptions NewOptions() =>
        new DbContextOptionsBuilder<CoreDbContext>()
            .UseInMemoryDatabase($"po-ownership-{Guid.NewGuid():N}", new InMemoryDatabaseRoot())
            .UseAutoConfigModel([typeof(CoreDbContext).Assembly])
            .EnableServiceProviderCaching(false)
            .Options;

    #endregion

    private sealed class NoKeyDataOwnerProvider : IDataOwnerProvider
    {
        public string? GetOwnershipKey() => null;
    }
}
