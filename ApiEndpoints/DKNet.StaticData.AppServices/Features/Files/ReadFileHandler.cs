using DKNet.EfCore.Specifications.Extensions;
using DKNet.EfCore.Specifications.Repositories;
using DKNet.StaticData.Domains.Features.Files.Entities;

namespace DKNet.StaticData.AppServices.Features.Files;

/// <summary>Reads one file's details through the owner filter.</summary>
public sealed class ReadFileHandler(IRepositorySpec repository)
{
    #region Methods

    /// <returns>The file's details, or <see langword="null" /> when it does not exist for this owner.</returns>
    public Task<StoredFileDto?> HandleAsync(Guid fileId, CancellationToken cancellationToken = default) =>
        repository.FirstOrDefaultAsync<StoredFile, StoredFileDto>(new StoredFileById(fileId), cancellationToken);

    #endregion
}
