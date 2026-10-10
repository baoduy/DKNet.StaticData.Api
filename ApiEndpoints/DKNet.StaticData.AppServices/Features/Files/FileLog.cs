using Microsoft.Extensions.Logging;

namespace DKNet.StaticData.AppServices.Features.Files;

/// <summary>
///     The file routes' log entries (design <c>05-quality.md</c>, Logs). None carries the owner, a file name or the
///     bytes; only the 2 entries about bytes left behind or missing name the storage key.
/// </summary>
public static partial class FileLog
{
    #region Methods

    [LoggerMessage(Level = LogLevel.Information,
        Message = "File uploaded: {FileId}, {SizeBytes} bytes of {ContentType}, by {CallerId}")]
    public static partial void FileUploaded(ILogger logger, Guid fileId, long sizeBytes, string contentType, string? callerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "File downloaded: {FileId}, {SizeBytes} bytes, by {CallerId}")]
    public static partial void FileDownloaded(ILogger logger, Guid fileId, long sizeBytes, string? callerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "File deleted: {FileId}, by {CallerId}")]
    public static partial void FileDeleted(ILogger logger, Guid fileId, string? callerId);

    /// <summary>The reason is <c>form</c>, <c>name</c>, <c>extension</c> or <c>size</c>.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "Upload refused: {Reason}, by {CallerId}")]
    public static partial void UploadRefused(ILogger logger, string reason, string? callerId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Bytes missing for file {FileId} under storage key {StorageKey}")]
    public static partial void BytesMissing(ILogger logger, Guid fileId, string storageKey);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Orphaned bytes of file {FileId} under storage key {StorageKey}: the delete failed with {ExceptionType}")]
    public static partial void BytesOrphaned(ILogger logger, Guid fileId, string storageKey, string exceptionType);

    [LoggerMessage(Level = LogLevel.Error, Message = "Blob storage cannot be reached: {ExceptionType}")]
    public static partial void BlobStorageUnreachable(ILogger logger, string exceptionType);

    [LoggerMessage(Level = LogLevel.Error, Message = "The database failed: {ExceptionType}")]
    public static partial void DatabaseFailed(ILogger logger, string exceptionType);

    #endregion
}
