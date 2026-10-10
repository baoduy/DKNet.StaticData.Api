namespace DKNet.StaticData.AppServices.Features.Files;

/// <summary>Why a file action did not happen; the route answers with the status each kind names.</summary>
public enum FileErrorKind
{
    /// <summary>The file does not exist for this owner: 404.</summary>
    NotFound,

    /// <summary>The upload's name, extension or empty size is refused: 400.</summary>
    Refused,

    /// <summary>The upload is over the size limit: 413.</summary>
    TooLarge,

    /// <summary>The file's bytes are missing from blob storage: 500.</summary>
    BytesMissing,

    /// <summary>Blob storage cannot be reached: 503.</summary>
    StorageUnavailable
}

/// <summary>A failed file action, as a FluentResults error carrying its <see cref="FileErrorKind" />.</summary>
public sealed class FileError(FileErrorKind kind, string message) : Error(message)
{
    #region Properties

    public FileErrorKind Kind { get; } = kind;

    #endregion
}
