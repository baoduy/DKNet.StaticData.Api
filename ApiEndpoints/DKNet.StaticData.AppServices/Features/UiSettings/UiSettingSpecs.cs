using DKNet.EfCore.Specifications.Definitions;
using DKNet.StaticData.Domains.Features.UiSettings.Entities;

namespace DKNet.StaticData.AppServices.Features.UiSettings;

/// <summary>One setting by id; the owner filter of the call applies as to every <see cref="UiSetting" /> query.</summary>
internal sealed class UiSettingById : Specification<UiSetting>
{
    #region Constructors

    public UiSettingById(Guid settingId) => WithFilter(s => s.Id == settingId);

    #endregion
}

/// <summary>The settings linked to one setting group.</summary>
internal sealed class UiSettingsInGroup : Specification<UiSetting>
{
    #region Constructors

    public UiSettingsInGroup(Guid groupId) => WithFilter(s => s.GroupId == groupId);

    #endregion
}
