using DKNet.EfCore.Specifications.Definitions;
using DKNet.StaticData.Domains.Features.Files.Entities;

namespace DKNet.StaticData.AppServices.Features.Files;

/// <summary>One file by id; the owner filter of the call applies as to every <see cref="StoredFile" /> query.</summary>
internal sealed class StoredFileById : Specification<StoredFile>
{
    #region Constructors

    public StoredFileById(Guid fileId) => WithFilter(f => f.Id == fileId);

    #endregion
}
