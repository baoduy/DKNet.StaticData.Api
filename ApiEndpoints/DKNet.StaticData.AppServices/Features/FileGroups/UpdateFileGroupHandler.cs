using DKNet.EfCore.Specifications.Extensions;
using DKNet.EfCore.Specifications.Repositories;
using DKNet.StaticData.AppServices.Features.Files;
using Microsoft.EntityFrameworkCore;

namespace DKNet.StaticData.AppServices.Features.FileGroups;

/// <summary>
///     Replaces a group's 3 fields when the caller names its current version. Checks answer in the order of rule R3:
///     the fields (400), the version tag sent at all (428), the group (404), the version (412).
/// </summary>
public sealed class UpdateFileGroupHandler(IRepositorySpec repository, ICallerAccessor caller)
{
    #region Methods

    /// <param name="groupId">The group id.</param>
    /// <param name="fields">The new fields.</param>
    /// <param name="ifMatch">
    ///     The version the <c>If-Match</c> header names, or <see langword="null" /> when the header is missing.
    /// </param>
    /// <param name="cancellationToken">The call's cancellation token.</param>
    public async Task<IResult<FileGroupDto>> HandleAsync(
        Guid groupId,
        FileGroupFields fields,
        int? ifMatch,
        CancellationToken cancellationToken = default)
    {
        if (fields.Refusal() is { } refusal)
        {
            return Result.Fail<FileGroupDto>(new FileError(FileErrorKind.Refused, refusal));
        }

        if (ifMatch is null)
        {
            return Result.Fail<FileGroupDto>(new FileError(FileErrorKind.VersionRequired,
                "The If-Match header with the group's version tag is required."));
        }

        var group = await repository.FirstOrDefaultAsync(new FileGroupById(groupId), cancellationToken);
        if (group is null)
        {
            return Result.Fail<FileGroupDto>(new FileError(FileErrorKind.NotFound, FileGroupAnswers.NotFound));
        }

        if (group.Version != ifMatch)
        {
            return Result.Fail<FileGroupDto>(new FileError(FileErrorKind.VersionStale, FileGroupAnswers.VersionStale));
        }

        group.Update(fields.Name!, fields.Purpose, fields.ExternalRef, caller.CallerId ?? string.Empty);
        try
        {
            await repository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another call changed the group between the read and this save: the version sent is no longer current.
            return Result.Fail<FileGroupDto>(new FileError(FileErrorKind.VersionStale, FileGroupAnswers.VersionStale));
        }

        return Result.Ok(group.Adapt<FileGroupDto>());
    }

    #endregion
}
