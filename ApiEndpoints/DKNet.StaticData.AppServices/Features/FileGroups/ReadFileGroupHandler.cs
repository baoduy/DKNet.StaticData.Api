using DKNet.EfCore.Specifications.Extensions;
using DKNet.EfCore.Specifications.Repositories;
using DKNet.StaticData.Domains.Features.FileGroups.Entities;

namespace DKNet.StaticData.AppServices.Features.FileGroups;

/// <summary>Reads one group through the owner filter.</summary>
public sealed class ReadFileGroupHandler(IRepositorySpec repository)
{
    #region Methods

    /// <returns>The group, or <see langword="null" /> when it does not exist for this owner.</returns>
    public Task<FileGroupDto?> HandleAsync(Guid groupId, CancellationToken cancellationToken = default) =>
        repository.FirstOrDefaultAsync<FileGroup, FileGroupDto>(new FileGroupById(groupId), cancellationToken);

    #endregion
}
