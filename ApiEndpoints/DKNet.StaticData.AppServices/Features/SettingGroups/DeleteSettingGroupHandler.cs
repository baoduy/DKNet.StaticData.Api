using DKNet.EfCore.Specifications.Extensions;
using DKNet.EfCore.Specifications.Repositories;
using DKNet.StaticData.AppServices.Features.FileGroups;
using DKNet.StaticData.AppServices.Features.Files;
using Microsoft.EntityFrameworkCore;

namespace DKNet.StaticData.AppServices.Features.SettingGroups;

/// <summary>
///     Deletes a setting group with no linked setting. The <c>If-Match</c> header is not read (rule R4). The database's
///     foreign key refuses the delete of a group with a linked setting, also one saved into it during this call: 409.
/// </summary>
public sealed class DeleteSettingGroupHandler(IRepositorySpec repository)
{
    #region Fields

    private const string HasSettings = "The setting group has linked settings. Unlink or delete them first.";

    #endregion

    #region Methods

    public async Task<IResultBase> HandleAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        var group = await repository.FirstOrDefaultAsync(new SettingGroupById(groupId), cancellationToken);
        if (group is null)
        {
            return Result.Fail(new FileError(FileErrorKind.NotFound, SettingGroupAnswers.NotFound));
        }

        repository.Delete(group);
        try
        {
            await repository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ForeignKeyViolation.IsIn(ex))
        {
            return Result.Fail(new FileError(FileErrorKind.Conflict, HasSettings));
        }

        return Result.Ok();
    }

    #endregion
}
