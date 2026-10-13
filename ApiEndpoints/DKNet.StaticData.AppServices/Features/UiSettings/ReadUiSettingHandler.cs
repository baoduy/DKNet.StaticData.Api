using DKNet.EfCore.Specifications.Extensions;
using DKNet.EfCore.Specifications.Repositories;
using DKNet.StaticData.Domains.Features.UiSettings.Entities;

namespace DKNet.StaticData.AppServices.Features.UiSettings;

/// <summary>Reads one setting through the owner filter.</summary>
public sealed class ReadUiSettingHandler(IRepositorySpec repository)
{
    #region Methods

    /// <returns>The setting, or <see langword="null" /> when it does not exist for this owner.</returns>
    public Task<UiSettingDto?> HandleAsync(Guid settingId, CancellationToken cancellationToken = default) =>
        repository.FirstOrDefaultAsync<UiSetting, UiSettingDto>(new UiSettingById(settingId), cancellationToken);

    #endregion
}
