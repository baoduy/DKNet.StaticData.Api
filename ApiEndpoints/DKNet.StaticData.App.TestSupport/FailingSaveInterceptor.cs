using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DKNet.StaticData.App.TestSupport;

/// <summary>
/// Makes saving the service's own records fail while <see cref="Enabled"/> is on, by throwing from the EF Core save
/// pipeline of <c>CoreDbContext</c> — the failure a real save that the database refuses surfaces as. The idempotency
/// store keeps its records through its own context, so it is not affected.
/// </summary>
public sealed class FailingSaveInterceptor : SaveChangesInterceptor
{
    public bool Enabled { get; set; }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ThrowIfEnabled();
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ThrowIfEnabled();
        return ValueTask.FromResult(result);
    }

    private void ThrowIfEnabled()
    {
        if (Enabled)
        {
            throw new DbUpdateException("simulated: saving file details to the database fails");
        }
    }
}
