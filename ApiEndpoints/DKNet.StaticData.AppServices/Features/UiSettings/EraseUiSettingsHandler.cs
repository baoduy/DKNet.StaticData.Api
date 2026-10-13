using DKNet.EfCore.Specifications.Definitions;
using DKNet.EfCore.Specifications.Extensions;
using DKNet.EfCore.Specifications.Repositories;
using DKNet.StaticData.AppServices.Features.FileGroups;
using DKNet.StaticData.AppServices.Features.Files;
using DKNet.StaticData.Domains.Features.SettingGroups.Entities;
using DKNet.StaticData.Domains.Features.UiSettings.Entities;
using Microsoft.EntityFrameworkCore;

namespace DKNet.StaticData.AppServices.Features.UiSettings;

/// <summary>
///     Deletes every setting, then every setting group, of the call's owner, all or nothing: one save, which EF Core runs
///     in one transaction with the settings first. Nothing is written before that save, so another call is never held
///     waiting on it. A setting saved into one of the groups after they were read makes the database refuse the group
///     delete; the whole save is then rolled back and answers 409.
/// </summary>
public sealed class EraseUiSettingsHandler(IRepositorySpec repository)
{
    #region Fields

    private const string SettingSavedMeanwhile =
        "A setting was saved into a setting group during the erase. Nothing was changed; repeat the erase.";

    #endregion

    #region Methods

    public async Task<IResultBase> HandleAsync(CancellationToken cancellationToken = default)
    {
        foreach (var setting in await repository.ToListAsync(new All<UiSetting>(), cancellationToken))
        {
            repository.Delete(setting);
        }

        foreach (var group in await repository.ToListAsync(new All<SettingGroup>(), cancellationToken))
        {
            repository.Delete(group);
        }

        try
        {
            await repository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ForeignKeyViolation.IsIn(ex))
        {
            return Result.Fail(new FileError(FileErrorKind.Conflict, SettingSavedMeanwhile));
        }

        return Result.Ok();
    }

    #endregion

    /// <summary>Every record of the type; the owner filter of the call still applies.</summary>
    private sealed class All<T> : Specification<T>
        where T : class;
}
