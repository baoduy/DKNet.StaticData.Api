using System.Security.Cryptography;
using DKNet.EfCore.Specifications.Extensions;
using DKNet.EfCore.Specifications.Repositories;
using DKNet.StaticData.AppServices.Features.FileGroups;
using DKNet.StaticData.Domains.Features.Files.Entities;
using DKNet.StaticData.Share.Options;
using DKNet.Svc.BlobStorage.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DKNet.StaticData.AppServices.Features.Files;

/// <summary>
///     Flow 1 of design <c>03-integration.md</c> from check 7 on: checks the file name, its extension, the size and the
///     group, stores the bytes under a key built from a new file id, then inserts the file's details. Details exist only
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

    /// <summary>Check 7, the file name: a required caller text of 1 to 255 characters (spec DRK-2245 §3).</summary>
    private static readonly InlineValidator<SentName> NameRule = new()
    {
        v => v.RuleFor(n => n.Name).CallerText(MaxFileNameLength, required: true)
    };

    #endregion

    #region Methods

    /// <param name="fileName">The file name the caller sent; any path in it is removed, then the spaces at both ends.</param>
    /// <param name="content">The buffered upload: read once for the checksum, then again to store it.</param>
    /// <param name="groupId">The file group of this owner to put the new file in, or <see langword="null" /> for none.</param>
    /// <param name="cancellationToken">The call's cancellation token.</param>
    /// <exception cref="Exception">Saving the details failed; the stored bytes were deleted first, best effort.</exception>
    public async Task<IResult<StoredFileDto>> HandleAsync(
        string fileName,
        Stream content,
        Guid? groupId = null,
        CancellationToken cancellationToken = default)
    {
        var sentName = WithoutPath(fileName);
        var name = TextRules.Trim(sentName)!;
        var extension = Path.GetExtension(name).ToLowerInvariant();
        if (Check(sentName, extension, content.Length) is { } refusal)
        {
            FileLog.UploadRefused(logger, refusal.Reason, caller.CallerId);
            return Result.Fail<StoredFileDto>(new FileError(refusal.Kind, refusal.Message));
        }

        // Check 9, after the file checks and before any byte is stored; another owner's group does not exist here.
        if (groupId is { } id && !await repository.AnyAsync(new FileGroupById(id), cancellationToken))
        {
            return Result.Fail<StoredFileDto>(new FileError(FileErrorKind.NotFound, FileGroupAnswers.NotFound));
        }

        var checksum = Convert.ToHexStringLower(await SHA256.HashDataAsync(content, cancellationToken));
        content.Position = 0;
        var file = StoredFile.Create(
            name, extension, name.GetContentTypeByExtension(), content.Length, checksum, caller.CallerId ?? string.Empty,
            groupId);

        if (!await TrySaveBytesAsync(file, content, cancellationToken))
        {
            return Result.Fail<StoredFileDto>(new FileError(FileErrorKind.StorageUnavailable, "Blob storage cannot be reached."));
        }

        if (!await TrySaveDetailsAsync(file, cancellationToken))
        {
            return Result.Fail<StoredFileDto>(new FileError(FileErrorKind.NotFound, FileGroupAnswers.NotFound));
        }

        FileLog.FileUploaded(logger, file.Id, file.SizeBytes, file.ContentType, caller.CallerId);
        return Result.Ok(file.Adapt<StoredFileDto>());
    }

    /// <summary>
    ///     Inserts the file's details. <see langword="false" /> when the database refused the group link: the group was
    ///     deleted after the check. Any failure deletes the stored bytes first, best effort; any other failure is rethrown.
    /// </summary>
    private async Task<bool> TrySaveDetailsAsync(StoredFile file, CancellationToken cancellationToken)
    {
        try
        {
            await repository.AddAsync(file, cancellationToken);
            await repository.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (ForeignKeyViolation.IsIn(ex))
        {
            await blobs.TryDeleteAsync(file, logger);
            return false;
        }
        catch
        {
            await blobs.TryDeleteAsync(file, logger);
            throw;
        }
    }

    /// <summary>The name after its last <c>/</c> or <c>\</c>: a browser may send a full client path.</summary>
    private static string WithoutPath(string fileName) => fileName[(fileName.LastIndexOfAny(['/', '\\']) + 1)..];

    /// <summary>
    ///     Checks 7 and 8 of the upload, in order, on the name as sent without its path and the extension of that name
    ///     trimmed; null when the file may be stored.
    /// </summary>
    private (string Reason, FileErrorKind Kind, string Message)? Check(string sentName, string extension, long size)
    {
        if (!NameRule.Validate(new SentName(sentName)).IsValid)
        {
            return ("name", FileErrorKind.Refused,
                "The file name must be 1 to 255 characters, not counting the spaces at both ends, with no control character.");
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

    private sealed record SentName(string? Name);
}
