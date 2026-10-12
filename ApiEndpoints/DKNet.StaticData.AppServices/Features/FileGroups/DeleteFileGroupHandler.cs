using DKNet.EfCore.Specifications.Extensions;
using DKNet.EfCore.Specifications.Repositories;
using DKNet.StaticData.AppServices.Features.Files;
using Microsoft.EntityFrameworkCore;

namespace DKNet.StaticData.AppServices.Features.FileGroups;

/// <summary>
///     Deletes a group with no linked file. The <c>If-Match</c> header is not read (rule R4). A file linked between
///     the check and the save makes the database refuse the delete, which answers the same 409. A group another call
///     deleted first is gone either way, so that delete succeeds too, as a racing file delete does (spec answer 1 A).
/// </summary>
public sealed class DeleteFileGroupHandler(IRepositorySpec repository)
{
    #region Fields

    private const string HasFiles = "The file group has linked files. Unlink or delete them first.";

    #endregion

    #region Methods

    public async Task<IResultBase> HandleAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        var group = await repository.FirstOrDefaultAsync(new FileGroupById(groupId), cancellationToken);
        if (group is null)
        {
            return Result.Fail(new FileError(FileErrorKind.NotFound, FileGroupAnswers.NotFound));
        }

        // An existence query, never a load: tracked linked files would make EF Core refuse the delete itself.
        if (await repository.AnyAsync(new StoredFilesInGroup(groupId), cancellationToken))
        {
            return Result.Fail(new FileError(FileErrorKind.Conflict, HasFiles));
        }

        repository.Delete(group);
        try
        {
            await repository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ForeignKeyViolation.IsIn(ex))
        {
            return Result.Fail(new FileError(FileErrorKind.Conflict, HasFiles));
        }

        return Result.Ok();
    }

    #endregion
}
