using DKNet.EfCore.Specifications.Definitions;
using DKNet.StaticData.Domains.Features.SettingGroups.Entities;

namespace DKNet.StaticData.AppServices.Features.SettingGroups;

/// <summary>One setting group by id; the owner filter of the call applies as to every <see cref="SettingGroup" /> query.</summary>
internal sealed class SettingGroupById : Specification<SettingGroup>
{
    #region Constructors

    public SettingGroupById(Guid groupId) => WithFilter(g => g.Id == groupId);

    #endregion
}
