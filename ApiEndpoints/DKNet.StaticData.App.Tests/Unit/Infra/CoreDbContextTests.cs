using DKNet.EfCore.DataAuthorization;
using DKNet.EfCore.Extensions.Extensions;
using DKNet.StaticData.Domains.Features.Files.Entities;
using DKNet.StaticData.Infra.Contexts;
using Microsoft.EntityFrameworkCore;

namespace DKNet.StaticData.App.Tests.Unit.Infra;

/// <summary>
/// A file is never saved for nobody: with no owner for the call, inserting a file fails before the database is asked,
/// on both save paths; with an owner it is saved.
/// </summary>
public class CoreDbContextTests
{
    [Fact]
    public async Task AFileWithNoOwnerIsRefusedBeforeItIsSaved()
    {
        await using var db = Context(owner: null);
        db.Add(NewFile());

        await Should.ThrowAsync<OwnershipRequiredException>(() => db.SaveChangesAsync());
        Should.Throw<OwnershipRequiredException>(() => db.SaveChanges());
        (await db.Set<StoredFile>().IgnoreQueryFilters().CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task AFileWithAnOwnerIsSaved()
    {
        await using var db = Context(owner: "customer-1");
        var file = NewFile();
        typeof(StoredFile).GetProperty(nameof(StoredFile.OwnedBy))!.SetValue(file, "customer-1");
        db.Add(file);

        (await db.SaveChangesAsync()).ShouldBe(1);
        (await db.Set<StoredFile>().CountAsync()).ShouldBe(1);
    }

    private static StoredFile NewFile() =>
        StoredFile.Create("passport.pdf", ".pdf", "application/pdf", 1024, new string('a', 64), "onboarding-svc");

    private static CoreDbContext Context(string? owner) =>
        new(new DbContextOptionsBuilder<CoreDbContext>()
                .UseInMemoryDatabase($"core-{Guid.NewGuid():N}")
                .UseAutoConfigModel([typeof(CoreDbContext).Assembly])
                .Options,
            [new Owner(owner)]);

    private sealed class Owner(string? key) : IDataOwnerProvider
    {
        public string? GetOwnershipKey() => key;
    }
}
