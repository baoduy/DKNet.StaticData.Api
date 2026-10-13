using DKNet.StaticData.Domains.Features.UiSettings.Entities;

namespace DKNet.StaticData.AppServices.Features.UiSettings;

/// <summary>
///     A UI setting as every setting answer carries it (spec DRK-2245 §3a); a field with no value is left out of the
///     JSON, and the value is written as the exact text it was stored with.
/// </summary>
public sealed record UiSettingDto
{
    #region Properties

    public Guid SettingId { get; init; }

    public string Owner { get; init; } = string.Empty;

    public string SettingKey { get; init; } = string.Empty;

    public string SettingType { get; init; } = string.Empty;

    public Guid? GroupId { get; init; }

    [JsonConverter(typeof(RawJsonConverter))]
    public string Value { get; init; } = string.Empty;

    public int SchemaVersion { get; init; }

    public int Version { get; init; }

    /// <summary>The caller id that created the setting.</summary>
    public string CreatedBy { get; init; } = string.Empty;

    public DateTimeOffset CreatedOn { get; init; }

    public string? UpdatedBy { get; init; }

    public DateTimeOffset? UpdatedOn { get; init; }

    #endregion
}

/// <summary>Maps <see cref="UiSetting" /> onto <see cref="UiSettingDto" />: the 2 fields whose names differ.</summary>
internal sealed class UiSettingDtoMapping : IRegister
{
    #region Methods

    public void Register(TypeAdapterConfig config) =>
        config.NewConfig<UiSetting, UiSettingDto>()
            .Map(d => d.SettingId, s => s.Id)
            .Map(d => d.Owner, s => s.OwnedBy);

    #endregion
}
