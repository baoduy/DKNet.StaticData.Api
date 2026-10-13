using DKNet.StaticData.Domains.Features.SettingGroups.Entities;

namespace DKNet.StaticData.AppServices.Features.SettingGroups;

/// <summary>A setting group as every group answer carries it (spec DRK-2245 §3a); a field with no value is left out of the JSON.</summary>
public sealed record SettingGroupDto
{
    #region Properties

    public Guid GroupId { get; init; }

    public string Owner { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    public int Version { get; init; }

    /// <summary>The caller id that created the group.</summary>
    public string CreatedBy { get; init; } = string.Empty;

    public DateTimeOffset CreatedOn { get; init; }

    public string? UpdatedBy { get; init; }

    public DateTimeOffset? UpdatedOn { get; init; }

    #endregion
}

/// <summary>Maps <see cref="SettingGroup" /> onto <see cref="SettingGroupDto" />: the 2 fields whose names differ.</summary>
internal sealed class SettingGroupDtoMapping : IRegister
{
    #region Methods

    public void Register(TypeAdapterConfig config) =>
        config.NewConfig<SettingGroup, SettingGroupDto>()
            .Map(d => d.GroupId, s => s.Id)
            .Map(d => d.Owner, s => s.OwnedBy);

    #endregion
}
