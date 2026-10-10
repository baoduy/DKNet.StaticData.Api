using DKNet.EfCore.DataAuthorization;
using DKNet.StaticData.Domains.Share;

namespace DKNet.StaticData.Domains.Features.Files.Entities;

/// <summary>
///     One stored file's metadata (design <c>04-data.md</c>, StoredFile). The bytes live in blob storage under
///     <see cref="StorageKey" />; the owner is stamped on save by DKNet's data-owner hook from the call's owner.
/// </summary>
public sealed class StoredFile : AggregateRoot, IOwnedBy
{
    #region Constructors

    private StoredFile(
        Guid id,
        string fileName,
        string contentType,
        long sizeBytes,
        string checksum,
        string storageKey,
        string createdBy)
        : base(id, createdBy)
    {
        FileName = fileName;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        Checksum = checksum;
        StorageKey = storageKey;
    }

    /// <summary>For EF Core materialisation only.</summary>
    private StoredFile()
    {
    }

    #endregion

    #region Properties

    public string Checksum { get; private set; } = string.Empty;

    public string ContentType { get; private set; } = string.Empty;

    public string FileName { get; private set; } = string.Empty;

    public string OwnedBy { get; private set; } = string.Empty;

    public long SizeBytes { get; private set; }

    /// <summary><c>files/&lt;Id&gt;&lt;lowercase extension&gt;</c>. Never leaves the service.</summary>
    public string StorageKey { get; private set; } = string.Empty;

    /// <summary>The app-managed version (ADR-0010), 1 at create; the concurrency token.</summary>
    public int Version { get; private set; } = 1;

    #endregion

    #region Methods

    /// <summary>
    ///     A new file with a new version-7 id, version 1, and its storage key built from that id and
    ///     <paramref name="extension" />.
    /// </summary>
    /// <param name="fileName">The file name, with no path.</param>
    /// <param name="extension">The file name's extension with its dot, already lowercase.</param>
    /// <param name="contentType">The content type the extension maps to.</param>
    /// <param name="sizeBytes">The size of the bytes.</param>
    /// <param name="checksum">The SHA-256 of the bytes, lowercase hex.</param>
    /// <param name="createdBy">The caller id.</param>
    public static StoredFile Create(
        string fileName,
        string extension,
        string contentType,
        long sizeBytes,
        string checksum,
        string createdBy)
    {
        var id = Guid.CreateVersion7();
        return new StoredFile(id, fileName, contentType, sizeBytes, checksum, $"files/{id}{extension}", createdBy);
    }

    #endregion
}
