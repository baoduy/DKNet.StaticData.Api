using DKNet.StaticData.Domains.Features.Files.Entities;
using DKNet.Svc.BlobStorage.Abstractions;
using Microsoft.Extensions.Logging;

namespace DKNet.StaticData.AppServices.Features.Files;

internal static class StoredFileBytes
{
    #region Methods

    /// <summary>
    ///     Deletes the file's bytes, best effort: a failure is logged as orphaned bytes, with the file id and the
    ///     storage key an operator removes them by, and never thrown.
    /// </summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Best effort by design (Flows 1 and 3): any failure leaves orphaned bytes, which are logged.")]
    public static async Task TryDeleteAsync(this IBlobService blobs, StoredFile file, ILogger logger)
    {
        try
        {
            await blobs.DeleteAsync(new BlobRequest(file.StorageKey), CancellationToken.None);
        }
        catch (Exception ex)
        {
            FileLog.BytesOrphaned(logger, file.Id, file.StorageKey, ex.GetType().Name);
        }
    }

    #endregion
}
