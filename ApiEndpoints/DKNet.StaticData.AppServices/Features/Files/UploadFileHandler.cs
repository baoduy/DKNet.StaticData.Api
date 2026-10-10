using System.Security.Cryptography;
using DKNet.EfCore.Specifications.Repositories;
using DKNet.StaticData.Domains.Features.Files.Entities;
using DKNet.StaticData.Share.Options;
using DKNet.Svc.BlobStorage.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DKNet.StaticData.AppServices.Features.Files;

/// <summary>
///     Flow 1 of design <c>03-integration.md</c> from check 7 on: checks the file name, its extension and the size,
///     stores the bytes under a key built from a new file id, then inserts the file's details. Details exist only
///     after their bytes are stored; bytes stored for details that could not be saved are deleted, best effort.
/// </summary>
public sealed class UploadFileHandler(
    IRepositorySpec repository,
    IBlobService blobs,
    IOptions<FileSettings> settings,
    ICallerAccessor caller,
    ILogger<UploadFileHandler> logger)
{
    #region Fields

    private const int MaxFileNameLength = 255;

    #endregion

    #region Methods

    /// <param name="fileName">The file name the caller sent; any path in it is removed.</param>
    /// <param name="content">The buffered upload: read once for the checksum, then again to store it.</param>
    /// <param name="cancellationToken">The call's cancellation token.</param>
    /// <exception cref="Exception">Saving the details failed; the stored bytes were deleted first, best effort.</exception>
    public async Task<IResult<StoredFileDto>> HandleAsync(
        string fileName,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        var name = WithoutPath(fileName);
        var extension = Path.GetExtension(name).ToLowerInvariant();
        if (Check(name, extension, content.Length) is { } refusal)
        {
            FileLog.UploadRefused(logger, refusal.Reason, caller.CallerId);
            return Result.Fail<StoredFileDto>(new FileError(refusal.Kind, refusal.Message));
        }

        var checksum = Convert.ToHexStringLower(await SHA256.HashDataAsync(content, cancellationToken));
        content.Position = 0;
        var file = StoredFile.Create(
            name, extension, name.GetContentTypeByExtension(), content.Length, checksum, caller.CallerId ?? string.Empty);

        if (!await TrySaveBytesAsync(file, content, cancellationToken))
        {
            return Result.Fail<StoredFileDto>(new FileError(FileErrorKind.StorageUnavailable, "Blob storage cannot be reached."));
        }

        try
        {
            await repository.AddAsync(file, cancellationToken);
            await repository.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await blobs.TryDeleteAsync(file, logger);
            throw;
        }

        FileLog.FileUploaded(logger, file.Id, file.SizeBytes, file.ContentType, caller.CallerId);
        return Result.Ok(file.Adapt<StoredFileDto>());
    }

    /// <summary>The name after its last <c>/</c> or <c>\</c>: a browser may send a full client path.</summary>
    private static string WithoutPath(string fileName) => fileName[(fileName.LastIndexOfAny(['/', '\\']) + 1)..];

    /// <summary>Checks 7 and 8 of the upload, in order; null when the file may be stored.</summary>
    private (string Reason, FileErrorKind Kind, string Message)? Check(string name, string extension, long size)
    {
        if (name.Length is 0 or > MaxFileNameLength || name.Any(char.IsControl))
        {
            return ("name", FileErrorKind.Refused, "The file name must be 1 to 255 characters with no control character.");
        }

        if (!settings.Value.AllowedExtensions.Contains(extension))
        {
            return ("extension", FileErrorKind.Refused, "The file type is not allowed.");
        }

        if (size == 0)
        {
            return ("size", FileErrorKind.Refused, "The file is empty.");
        }

        return size > FileSettings.MaxFileSizeBytes
            ? ("size", FileErrorKind.TooLarge, $"The file is over {FileSettings.MaxFileSizeBytes} bytes.")
            : null;
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Any blob storage failure answers 503 and stores no details (Flow 1).")]
    private async Task<bool> TrySaveBytesAsync(StoredFile file, Stream content, CancellationToken cancellationToken)
    {
        try
        {
            await blobs.SaveAsync(
                new BlobDetails.BlobStreamData(file.StorageKey, content) { ContentType = file.ContentType },
                cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            FileLog.BlobStorageUnreachable(logger, ex.GetType().Name);
            await blobs.TryDeleteAsync(file, logger);
            return false;
        }
    }

    #endregion
}
