using DKNet.EfCore.Specifications.Definitions;
using DKNet.StaticData.Domains.Features.FileGroups.Entities;
using DKNet.StaticData.Domains.Features.Files.Entities;

namespace DKNet.StaticData.AppServices.Features.FileGroups;

/// <summary>One group by id; the owner filter of the call applies as to every <see cref="FileGroup" /> query.</summary>
internal sealed class FileGroupById : Specification<FileGroup>
{
    #region Constructors

    public FileGroupById(Guid groupId) => WithFilter(g => g.Id == groupId);

    #endregion
}

/// <summary>The files linked to one group.</summary>
internal sealed class StoredFilesInGroup : Specification<StoredFile>
{
    #region Constructors

    public StoredFilesInGroup(Guid groupId) => WithFilter(f => f.GroupId == groupId);

    #endregion
}
