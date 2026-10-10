using System.Diagnostics.CodeAnalysis;
using DKNet.StaticData.Infra.Contexts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DKNet.StaticData.Infra.Services;

/// <summary>
/// Deletes the idempotency records whose <c>ExpiresAt</c> has passed, once an hour in every replica (ADR-0012): the
/// DKNet idempotency store never deletes a row. Overlapping sweeps across replicas are harmless — each deletes only
/// rows that have already expired. A failed sweep logs a warning and the next hour tries again.
/// </summary>
internal sealed partial class IdempotencySweep(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<IdempotencySweep> logger) : BackgroundService
{
    #region Fields

    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    #endregion

    #region Methods

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await SweepAsync(stoppingToken);
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "A failed sweep must never stop the host: it is logged and the next hour retries.")]
    internal async Task SweepAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
            var now = clock.GetUtcNow();
            // The table is the idempotency store's own (package-owned, default schema, the same name and columns on
            // both databases); double-quoted identifiers work on Postgres and on SQL Server's default QUOTED_IDENTIFIER.
            var deleted = await db.Database.ExecuteSqlAsync(
                $"DELETE FROM \"IdempotencyKeys\" WHERE \"ExpiresAt\" < {now}", cancellationToken);
            LogSwept(logger, deleted);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The host is stopping.
        }
        catch (Exception ex)
        {
            LogSweepFailed(logger, ex.GetType().Name);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Idempotency sweep deleted {Count} expired records")]
    private static partial void LogSwept(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Idempotency sweep failed with {ExceptionType}; the next sweep runs in 1 hour")]
    private static partial void LogSweepFailed(ILogger logger, string exceptionType);

    #endregion
}
