using DKNet.StaticData.Domains.Features.FileGroups.Entities;

namespace DKNet.StaticData.AppServices.Features.FileGroups;

/// <summary>A file group as every group answer carries it (spec DRK-2235 §3a); a field with no value is left out of the JSON.</summary>
public sealed record FileGroupDto
{
    #region Properties

    public Guid GroupId { get; init; }

    /// <summary>The owner, as sent at create.</summary>
    public string Owner { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Purpose { get; init; }

    public string? ExternalRef { get; init; }

    public int Version { get; init; }

    /// <summary>The caller id that created the group.</summary>
    public string CreatedBy { get; init; } = string.Empty;

    public DateTimeOffset CreatedOn { get; init; }

    public string? UpdatedBy { get; init; }

    public DateTimeOffset? UpdatedOn { get; init; }

    #endregion
}

/// <summary>Maps <see cref="FileGroup" /> onto <see cref="FileGroupDto" />: the 2 fields whose names differ.</summary>
internal sealed class FileGroupDtoMapping : IRegister
{
    #region Methods

    public void Register(TypeAdapterConfig config) =>
        config.NewConfig<FileGroup, FileGroupDto>()
            .Map(d => d.GroupId, s => s.Id)
            .Map(d => d.Owner, s => s.OwnedBy);

    #endregion
}
