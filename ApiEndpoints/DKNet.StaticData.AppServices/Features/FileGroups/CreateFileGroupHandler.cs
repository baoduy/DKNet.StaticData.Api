using DKNet.EfCore.Specifications.Repositories;
using DKNet.StaticData.Domains.Features.FileGroups.Entities;

namespace DKNet.StaticData.AppServices.Features.FileGroups;

/// <summary>
///     Creates the group at version 1 from its fields, trimmed; <see cref="FileGroupFieldsValidator" /> checked them
///     (R1, R2) before this handler runs.
/// </summary>
public sealed class CreateFileGroupHandler(IRepositorySpec repository, ICallerAccessor caller)
{
    #region Methods

    public async Task<IResult<FileGroupDto>> HandleAsync(FileGroupFields fields, CancellationToken cancellationToken = default)
    {
        var group = FileGroup.Create(
            TextRules.Trim(fields.Name)!, TextRules.Trim(fields.Purpose), TextRules.Trim(fields.ExternalRef),
            caller.CallerId ?? string.Empty);
        await repository.AddAsync(group, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return Result.Ok(group.Adapt<FileGroupDto>());
    }

    #endregion
}
