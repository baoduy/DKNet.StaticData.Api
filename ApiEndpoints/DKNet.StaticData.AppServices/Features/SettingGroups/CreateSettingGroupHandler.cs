using DKNet.EfCore.Specifications.Repositories;
using DKNet.StaticData.AppServices.Features.FileGroups;
using DKNet.StaticData.AppServices.Features.Files;
using DKNet.StaticData.Domains.Features.SettingGroups.Entities;
using Microsoft.EntityFrameworkCore;

namespace DKNet.StaticData.AppServices.Features.SettingGroups;

/// <summary>
///     Creates a setting group at version 1 with the trimmed texts; the fields were checked by their validator. A name
///     the owner already uses answers 409: the database's unique key refuses it.
/// </summary>
public sealed class CreateSettingGroupHandler(IRepositorySpec repository, ICallerAccessor caller)
{
    #region Methods

    public async Task<IResult<SettingGroupDto>> HandleAsync(SettingGroupFields fields, CancellationToken cancellationToken = default)
    {
        var group = SettingGroup.Create(TextRules.Trim(fields.Name)!, TextRules.Trim(fields.Description), caller.CallerId ?? string.Empty);
        await repository.AddAsync(group, cancellationToken);
        try
        {
            await repository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (UniqueViolation.IsIn(ex))
        {
            return Result.Fail<SettingGroupDto>(new FileError(FileErrorKind.Conflict, SettingGroupAnswers.NameUsed));
        }

        return Result.Ok(group.Adapt<SettingGroupDto>());
    }

    #endregion
}
