using DKNet.EfCore.Specifications.Repositories;
using DKNet.StaticData.AppServices.Features.Files;
using DKNet.StaticData.Domains.Features.FileGroups.Entities;

namespace DKNet.StaticData.AppServices.Features.FileGroups;

/// <summary>Checks the group fields (R1, R2), then creates the group at version 1.</summary>
public sealed class CreateFileGroupHandler(IRepositorySpec repository, ICallerAccessor caller)
{
    #region Methods

    public async Task<IResult<FileGroupDto>> HandleAsync(FileGroupFields fields, CancellationToken cancellationToken = default)
    {
        if (fields.Refusal() is { } refusal)
        {
            return Result.Fail<FileGroupDto>(new FileError(FileErrorKind.Refused, refusal));
        }

        var group = FileGroup.Create(fields.Name!, fields.Purpose, fields.ExternalRef, caller.CallerId ?? string.Empty);
        await repository.AddAsync(group, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return Result.Ok(group.Adapt<FileGroupDto>());
    }

    #endregion
}
