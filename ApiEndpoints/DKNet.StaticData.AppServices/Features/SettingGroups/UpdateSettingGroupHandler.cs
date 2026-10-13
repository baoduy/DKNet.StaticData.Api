using DKNet.EfCore.Specifications.Extensions;
using DKNet.EfCore.Specifications.Repositories;
using DKNet.StaticData.AppServices.Features.FileGroups;
using DKNet.StaticData.AppServices.Features.Files;
using Microsoft.EntityFrameworkCore;

namespace DKNet.StaticData.AppServices.Features.SettingGroups;

/// <summary>
///     Replaces a setting group's name and description when the caller names its current version. The fields were checked
///     by their validator; the rest answers in the order of rule R3: the version tag sent at all (428), the group (404),
///     the version (412), then the name used by another group of the owner (409), which the database's unique key
///     refuses on save.
/// </summary>
public sealed class UpdateSettingGroupHandler(IRepositorySpec repository, ICallerAccessor caller)
{
    #region Methods

    /// <param name="groupId">The group id.</param>
    /// <param name="fields">The new fields.</param>
    /// <param name="ifMatch">
    ///     The version the <c>If-Match</c> header names, or <see langword="null" /> when the header is missing.
    /// </param>
    /// <param name="cancellationToken">The call's cancellation token.</param>
    public async Task<IResult<SettingGroupDto>> HandleAsync(
        Guid groupId,
        SettingGroupFields fields,
        int? ifMatch,
        CancellationToken cancellationToken = default)
    {
        if (ifMatch is null)
        {
            return Result.Fail<SettingGroupDto>(new FileError(FileErrorKind.VersionRequired,
                "The If-Match header with the setting group's version tag is required."));
        }

        var group = await repository.FirstOrDefaultAsync(new SettingGroupById(groupId), cancellationToken);
        if (group is null)
        {
            return Result.Fail<SettingGroupDto>(new FileError(FileErrorKind.NotFound, SettingGroupAnswers.NotFound));
        }

        if (group.Version != ifMatch)
        {
            return Result.Fail<SettingGroupDto>(new FileError(FileErrorKind.VersionStale, SettingGroupAnswers.VersionStale));
        }

        group.Update(TextRules.Trim(fields.Name)!, TextRules.Trim(fields.Description), caller.CallerId ?? string.Empty);
        try
        {
            await repository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another call changed the group between the read and this save: the version sent is no longer current.
            return Result.Fail<SettingGroupDto>(new FileError(FileErrorKind.VersionStale, SettingGroupAnswers.VersionStale));
        }
        catch (DbUpdateException ex) when (UniqueViolation.IsIn(ex))
        {
            return Result.Fail<SettingGroupDto>(new FileError(FileErrorKind.Conflict, SettingGroupAnswers.NameUsed));
        }

        return Result.Ok(group.Adapt<SettingGroupDto>());
    }

    #endregion
}
