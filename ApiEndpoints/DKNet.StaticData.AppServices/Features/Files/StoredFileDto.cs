using DKNet.StaticData.Domains.Features.Files.Entities;

namespace DKNet.StaticData.AppServices.Features.Files;

/// <summary>
///     A stored file's metadata as every file answer carries it (spec DRK-2206 §3a). The storage key is never in it;
///     a field with no value is left out of the JSON.
/// </summary>
public sealed record StoredFileDto
{
    #region Properties

    public Guid FileId { get; init; }

    /// <summary>The owner, as sent at create.</summary>
    public string Owner { get; init; } = string.Empty;

    public string FileName { get; init; } = string.Empty;

    /// <summary>Derived from the extension, never from the caller.</summary>
    public string ContentType { get; init; } = string.Empty;

    public long SizeBytes { get; init; }

    /// <summary>SHA-256, 64 lowercase hex characters.</summary>
    public string Checksum { get; init; } = string.Empty;

    public int Version { get; init; }

    /// <summary>The caller id that created the file.</summary>
    public string CreatedBy { get; init; } = string.Empty;

    public DateTimeOffset CreatedOn { get; init; }

    public string? UpdatedBy { get; init; }

    public DateTimeOffset? UpdatedOn { get; init; }

    #endregion
}

/// <summary>Maps <see cref="StoredFile" /> onto <see cref="StoredFileDto" />: the 2 fields whose names differ.</summary>
internal sealed class StoredFileDtoMapping : IRegister
{
    #region Methods

    public void Register(TypeAdapterConfig config) =>
        config.NewConfig<StoredFile, StoredFileDto>()
            .Map(d => d.FileId, s => s.Id)
            .Map(d => d.Owner, s => s.OwnedBy);

    #endregion
}
