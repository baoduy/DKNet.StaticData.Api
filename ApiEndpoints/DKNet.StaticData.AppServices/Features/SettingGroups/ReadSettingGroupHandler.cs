using DKNet.EfCore.Specifications.Extensions;
using DKNet.EfCore.Specifications.Repositories;
using DKNet.StaticData.Domains.Features.SettingGroups.Entities;

namespace DKNet.StaticData.AppServices.Features.SettingGroups;

/// <summary>Reads one setting group through the owner filter.</summary>
public sealed class ReadSettingGroupHandler(IRepositorySpec repository)
{
    #region Methods

    /// <returns>The group, or <see langword="null" /> when it does not exist for this owner.</returns>
    public Task<SettingGroupDto?> HandleAsync(Guid groupId, CancellationToken cancellationToken = default) =>
        repository.FirstOrDefaultAsync<SettingGroup, SettingGroupDto>(new SettingGroupById(groupId), cancellationToken);

    #endregion
}
