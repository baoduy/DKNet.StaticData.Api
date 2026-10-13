using DKNet.EfCore.Specifications.Extensions;
using DKNet.EfCore.Specifications.Repositories;
using DKNet.StaticData.AppServices.Features.FileGroups;
using DKNet.StaticData.AppServices.Features.Files;
using DKNet.StaticData.AppServices.Features.SettingGroups;
using Microsoft.EntityFrameworkCore;

namespace DKNet.StaticData.AppServices.Features.UiSettings;

/// <summary>
///     Replaces a setting's type, group, value and schema version when the caller names its current version; the key
///     never changes. The fields were checked by their validator; the rest answers in the order of rule R2: the version
///     tag sent at all (428), the setting (404), the group (404), the version (412). A group deleted between the check
///     and the save answers the same 404.
/// </summary>
public sealed class UpdateUiSettingHandler(IRepositorySpec repository, ICallerAccessor caller)
{
    #region Methods

    /// <param name="settingId">The setting id.</param>
    /// <param name="fields">The new fields.</param>
    /// <param name="ifMatch">
    ///     The version the <c>If-Match</c> header names, or <see langword="null" /> when the header is missing.
    /// </param>
    /// <param name="cancellationToken">The call's cancellation token.</param>
    public async Task<IResult<UiSettingDto>> HandleAsync(
        Guid settingId,
        UiSettingFields fields,
        int? ifMatch,
        CancellationToken cancellationToken = default)
    {
        if (ifMatch is null)
        {
            return Result.Fail<UiSettingDto>(new FileError(FileErrorKind.VersionRequired,
                "The If-Match header with the setting's version tag is required."));
        }

        var setting = await repository.FirstOrDefaultAsync(new UiSettingById(settingId), cancellationToken);
        if (setting is null)
        {
            return Result.Fail<UiSettingDto>(new FileError(FileErrorKind.NotFound, UiSettingAnswers.NotFound));
        }

        if (fields.GroupId is { } groupId && !await repository.AnyAsync(new SettingGroupById(groupId), cancellationToken))
        {
            return Result.Fail<UiSettingDto>(new FileError(FileErrorKind.NotFound, SettingGroupAnswers.NotFound));
        }

        if (setting.Version != ifMatch)
        {
            return Result.Fail<UiSettingDto>(new FileError(FileErrorKind.VersionStale, UiSettingAnswers.VersionStale));
        }

        setting.Update(TextRules.Trim(fields.SettingType)!, fields.GroupId, fields.Value!, fields.SchemaVersion ?? 1,
            caller.CallerId ?? string.Empty);
        try
        {
            await repository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another call changed the setting between the read and this save: the version sent is no longer current.
            return Result.Fail<UiSettingDto>(new FileError(FileErrorKind.VersionStale, UiSettingAnswers.VersionStale));
        }
        catch (DbUpdateException ex) when (ForeignKeyViolation.IsIn(ex))
        {
            return Result.Fail<UiSettingDto>(new FileError(FileErrorKind.NotFound, SettingGroupAnswers.NotFound));
        }

        return Result.Ok(setting.Adapt<UiSettingDto>());
    }

    #endregion
}
