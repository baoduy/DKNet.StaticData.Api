using DKNet.EfCore.Specifications.Extensions;
using DKNet.EfCore.Specifications.Repositories;
using DKNet.StaticData.AppServices.Features.Files;

namespace DKNet.StaticData.AppServices.Features.UiSettings;

/// <summary>
///     Deletes one setting. The <c>If-Match</c> header is not read (rule R4). A setting another call deleted first is gone
///     either way, so that delete succeeds too.
/// </summary>
public sealed class DeleteUiSettingHandler(IRepositorySpec repository)
{
    #region Methods

    public async Task<IResultBase> HandleAsync(Guid settingId, CancellationToken cancellationToken = default)
    {
        var setting = await repository.FirstOrDefaultAsync(new UiSettingById(settingId), cancellationToken);
        if (setting is null)
        {
            return Result.Fail(new FileError(FileErrorKind.NotFound, UiSettingAnswers.NotFound));
        }

        repository.Delete(setting);
        await repository.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }

    #endregion
}
