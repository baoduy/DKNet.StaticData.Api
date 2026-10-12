using DKNet.EfCore.Specifications.Extensions;
using DKNet.EfCore.Specifications.Repositories;
using DKNet.StaticData.AppServices.Features.FileGroups;
using Microsoft.EntityFrameworkCore;

namespace DKNet.StaticData.AppServices.Features.Files;

/// <summary>
///     Links a file to a file group of the same owner, or unlinks it, when the caller names the file's current version.
///     Checks answer in the order of rule R1 from the version tag on: the version tag sent at all (428), the file (404),
///     the group (404), the version (412). Every accepted change raises the version by 1, the same group included, and
///     writes no log entry (rules R2-R4).
/// </summary>
public sealed class LinkFileHandler(IRepositorySpec repository, ICallerAccessor caller)
{
    #region Fields

    private const string VersionStale = "The version tag does not name the file's current version.";

    #endregion

    #region Methods

    /// <param name="fileId">The file id.</param>
    /// <param name="groupId">The group to link the file to, or <see langword="null" /> to unlink it.</param>
    /// <param name="ifMatch">
    ///     The version the <c>If-Match</c> header names, or <see langword="null" /> when the header is missing.
    /// </param>
    /// <param name="cancellationToken">The call's cancellation token.</param>
    public async Task<IResult<StoredFileDto>> HandleAsync(
        Guid fileId,
        Guid? groupId,
        int? ifMatch,
        CancellationToken cancellationToken = default)
    {
        if (ifMatch is null)
        {
            return Fail(FileErrorKind.VersionRequired, "The If-Match header with the file's version tag is required.");
        }

        var file = await repository.FirstOrDefaultAsync(new StoredFileById(fileId), cancellationToken);
        if (file is null)
        {
            return Fail(FileErrorKind.NotFound, "The file does not exist.");
        }

        // Another owner's group does not exist here (rule R3).
        if (groupId is { } id && !await repository.AnyAsync(new FileGroupById(id), cancellationToken))
        {
            return Fail(FileErrorKind.NotFound, FileGroupAnswers.NotFound);
        }

        if (file.Version != ifMatch)
        {
            return Fail(FileErrorKind.VersionStale, VersionStale);
        }

        file.LinkTo(groupId, caller.CallerId ?? string.Empty);
        try
        {
            await repository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another call changed the file between the read and this save: the version sent is no longer current.
            return Fail(FileErrorKind.VersionStale, VersionStale);
        }
        catch (DbUpdateException ex) when (ForeignKeyViolation.IsIn(ex))
        {
            // The group was deleted between the check above and this save.
            return Fail(FileErrorKind.NotFound, FileGroupAnswers.NotFound);
        }

        return Result.Ok(file.Adapt<StoredFileDto>());
    }

    private static IResult<StoredFileDto> Fail(FileErrorKind kind, string message) =>
        Result.Fail<StoredFileDto>(new FileError(kind, message));

    #endregion
}
