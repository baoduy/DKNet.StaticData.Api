using DKNet.EfCore.Specifications.Extensions;
using DKNet.EfCore.Specifications.Repositories;
using DKNet.StaticData.AppServices.Features.FileGroups;
using DKNet.StaticData.AppServices.Features.Files;
using DKNet.StaticData.AppServices.Features.SettingGroups;
using DKNet.StaticData.Domains.Features.UiSettings.Entities;
using Microsoft.EntityFrameworkCore;

namespace DKNet.StaticData.AppServices.Features.UiSettings;

/// <summary>
///     Creates a setting at version 1 with the trimmed texts and the value as sent; the fields were checked by their
///     validator. The rest answers in the order of rule R1: the group (404), then the setting key already used (409). A
///     group deleted, or the same key saved, between those checks and this save answers the same 404 or 409.
/// </summary>
public sealed class CreateUiSettingHandler(IRepositorySpec repository, ICallerAccessor caller)
{
    #region Methods

    public async Task<IResult<UiSettingDto>> HandleAsync(NewUiSettingFields fields, CancellationToken cancellationToken = default)
    {
        if (fields.GroupId is { } groupId && !await repository.AnyAsync(new SettingGroupById(groupId), cancellationToken))
        {
            return Result.Fail<UiSettingDto>(new FileError(FileErrorKind.NotFound, SettingGroupAnswers.NotFound));
        }

        var settingKey = TextRules.Trim(fields.SettingKey)!;
        if (await repository.AnyAsync(new UiSettingByKey(settingKey), cancellationToken))
        {
            return Result.Fail<UiSettingDto>(new FileError(FileErrorKind.Conflict, UiSettingAnswers.AlreadyExists));
        }

        var setting = UiSetting.Create(settingKey, TextRules.Trim(fields.SettingType)!, fields.GroupId, fields.Value!,
            fields.SchemaVersion ?? 1, caller.CallerId ?? string.Empty);
        await repository.AddAsync(setting, cancellationToken);
        try
        {
            await repository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (UniqueViolation.IsIn(ex))
        {
            return Result.Fail<UiSettingDto>(new FileError(FileErrorKind.Conflict, UiSettingAnswers.AlreadyExists));
        }
        catch (DbUpdateException ex) when (ForeignKeyViolation.IsIn(ex))
        {
            return Result.Fail<UiSettingDto>(new FileError(FileErrorKind.NotFound, SettingGroupAnswers.NotFound));
        }

        return Result.Ok(setting.Adapt<UiSettingDto>());
    }

    #endregion
}
