using DKNet.EfCore.Specifications.Extensions;
using DKNet.EfCore.Specifications.Repositories;
using DKNet.StaticData.AppServices.Features.FileGroups;
using DKNet.StaticData.AppServices.Features.Files;
using DKNet.StaticData.Domains.Features.SettingGroups.Entities;
using Microsoft.EntityFrameworkCore;

namespace DKNet.StaticData.AppServices.Features.SettingGroups;

/// <summary>
///     Creates a setting group at version 1 with the trimmed texts; the fields were checked by their validator. A name
///     the owner already uses answers 409, also when another create saves it between the check and this save.
/// </summary>
public sealed class CreateSettingGroupHandler(IRepositorySpec repository, ICallerAccessor caller)
{
    #region Methods

    public async Task<IResult<SettingGroupDto>> HandleAsync(SettingGroupFields fields, CancellationToken cancellationToken = default)
    {
        var name = TextRules.Trim(fields.Name)!;
        if (await repository.AnyAsync(new SettingGroupByName(name), cancellationToken))
        {
            return Result.Fail<SettingGroupDto>(new FileError(FileErrorKind.Conflict, SettingGroupAnswers.NameUsed));
        }

        var group = SettingGroup.Create(name, TextRules.Trim(fields.Description), caller.CallerId ?? string.Empty);
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
