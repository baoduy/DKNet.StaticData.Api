using DKNet.EfCore.Specifications.Extensions;
using DKNet.EfCore.Specifications.Repositories;
using DKNet.Svc.BlobStorage.Abstractions;
using Microsoft.Extensions.Logging;

namespace DKNet.StaticData.AppServices.Features.Files;

/// <summary>An open read stream on a file's bytes, with what the answer needs to describe them.</summary>
public sealed record FileContent(Stream Content, string FileName, string ContentType, long SizeBytes);

/// <summary>
///     Flow 2 of design <c>03-integration.md</c>: reads the file through the owner filter and opens a read stream on its
///     bytes. Missing bytes are an error of the service (500); blob storage that cannot be reached is 503.
/// </summary>
public sealed class DownloadFileHandler(
    IRepositorySpec repository,
    IBlobService blobs,
    ICallerAccessor caller,
    ILogger<DownloadFileHandler> logger)
{
    #region Methods

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Any blob storage failure other than missing bytes answers 503 (Flow 2).")]
    public async Task<IResult<FileContent>> HandleAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var file = await repository.FirstOrDefaultAsync(new StoredFileById(fileId), cancellationToken);
        if (file is null)
        {
            return Result.Fail<FileContent>(new FileError(FileErrorKind.NotFound, "The file does not exist."));
        }

        Stream? content;
        try
        {
            content = await blobs.OpenReadAsync(new BlobRequest(file.StorageKey), cancellationToken);
        }
        // FileNotFoundException derives from IOException, the type an unreachable store throws too: caught first.
        catch (FileNotFoundException)
        {
            content = null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            FileLog.BlobStorageUnreachable(logger, ex.GetType().Name);
            return Result.Fail<FileContent>(new FileError(FileErrorKind.StorageUnavailable, "Blob storage cannot be reached."));
        }

        if (content is null)
        {
            FileLog.BytesMissing(logger, file.Id, file.StorageKey);
            return Result.Fail<FileContent>(new FileError(FileErrorKind.BytesMissing, "The file's bytes are missing."));
        }

        FileLog.FileDownloaded(logger, file.Id, file.SizeBytes, caller.CallerId);
        return Result.Ok(new FileContent(content, file.FileName, file.ContentType, file.SizeBytes));
    }

    #endregion
}
