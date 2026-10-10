using DKNet.EfCore.Extensions.Extensions;
using DKNet.EfCore.Specifications;
using DKNet.StaticData.AppServices.Features.Files;
using DKNet.StaticData.AppServices.Share;
using DKNet.StaticData.Domains.Features.Files.Entities;
using DKNet.StaticData.Infra.Contexts;
using DKNet.Svc.BlobStorage.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DKNet.StaticData.App.Tests.Unit.Files;

/// <summary>A delete that loses a race with another delete of the same file answers "does not exist" (404).</summary>
public class DeleteFileHandlerTests
{
    [Fact]
    public async Task AFileDeletedBetweenTheReadAndTheSaveDoesNotExist()
    {
        var database = $"delete-{Guid.NewGuid():N}";
        var file = StoredFile.Create("passport.pdf", ".pdf", "application/pdf", 10, new string('a', 64), "onboarding-svc");
        await using (var seed = Context(database))
        {
            seed.Add(file);
            await seed.SaveChangesAsync();
        }

        var services = new ServiceCollection()
            .AddDbContext<CoreDbContext>(o => Configure(o, database).AddInterceptors(new DeleteRowFirst(database, file.Id)))
            .AddSpecRepo<CoreDbContext>()
            .BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        var blobs = new Mock<IBlobService>();
        var handler = new DeleteFileHandler(
            scope.ServiceProvider.GetRequiredService<DKNet.EfCore.Specifications.Repositories.IRepositorySpec>(),
            blobs.Object,
            Mock.Of<ICallerAccessor>(c => c.CallerId == "onboarding-svc"),
            NullLogger<DeleteFileHandler>.Instance);

        var result = await handler.HandleAsync(file.Id);

        result.Errors.OfType<FileError>().ShouldHaveSingleItem().Kind.ShouldBe(FileErrorKind.NotFound);
        blobs.VerifyNoOtherCalls();
    }

    private static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, string database) =>
        options.UseInMemoryDatabase(database).UseAutoConfigModel([typeof(CoreDbContext).Assembly]);

    private static CoreDbContext Context(string database) =>
        new(Configure(new DbContextOptionsBuilder<CoreDbContext>(), database).Options);

    /// <summary>Deletes the row through another context just before the handler's own save runs.</summary>
    private sealed class DeleteRowFirst(string database, Guid fileId) : SaveChangesInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            await using var other = Context(database);
            other.Remove(await other.Set<StoredFile>().SingleAsync(f => f.Id == fileId, cancellationToken));
            await other.SaveChangesAsync(cancellationToken);
            return result;
        }
    }
}
