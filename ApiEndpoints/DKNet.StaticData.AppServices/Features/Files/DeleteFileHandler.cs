using DKNet.EfCore.Specifications.Extensions;
using DKNet.EfCore.Specifications.Repositories;
using DKNet.Svc.BlobStorage.Abstractions;
using Microsoft.Extensions.Logging;

namespace DKNet.StaticData.AppServices.Features.Files;

/// <summary>
///     Flow 3 of design <c>03-integration.md</c>: deletes the file's details, then its bytes. A failed byte delete still
///     deletes the file — it no longer exists for any caller — and its bytes are logged as orphaned.
/// </summary>
public sealed class DeleteFileHandler(
    IRepositorySpec repository,
    IBlobService blobs,
    ICallerAccessor caller,
    ILogger<DeleteFileHandler> logger)
{
    #region Methods

    public async Task<IResultBase> HandleAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var file = await repository.FirstOrDefaultAsync(new StoredFileById(fileId), cancellationToken);
        if (file is null)
        {
            return Result.Fail(new FileError(FileErrorKind.NotFound, "The file does not exist."));
        }

        repository.Delete(file);
        await repository.SaveChangesAsync(cancellationToken);
        await blobs.TryDeleteAsync(file, logger);

        FileLog.FileDeleted(logger, file.Id, caller.CallerId);
        return Result.Ok();
    }

    #endregion
}
