using DKNet.EfCore.Extensions.Extensions;
using Microsoft.Extensions.Logging;

namespace DKNet.StaticData.Infra.Services;

/// <summary>
/// What a save does when another call changed or deleted one of its rows first. A changed row keeps the other call's
/// change: the save fails with <see cref="DbUpdateConcurrencyException" />, so a version-guarded update answers 412
/// instead of overwriting it (ADR-0010). A delete of a row already gone keeps DKNet's own handling, which lets it
/// succeed.
/// </summary>
internal sealed class VersionConflictHandler(ILogger<EfCoreExceptionHandler> logger) : IEfCoreExceptionHandler
{
    #region Methods

    public Task<EfConcurrencyResolution> HandlingAsync(
        DbContext context,
        DbUpdateConcurrencyException exception,
        CancellationToken cancellationToken = default) =>
        exception.Entries.Any(e => e.State == EntityState.Modified)
            ? Task.FromResult(EfConcurrencyResolution.RethrowException)
            : new EfCoreExceptionHandler(logger).HandlingAsync(context, exception, cancellationToken);

    #endregion
}
