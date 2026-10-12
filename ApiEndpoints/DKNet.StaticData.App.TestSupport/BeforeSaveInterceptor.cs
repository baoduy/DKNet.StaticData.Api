using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DKNet.StaticData.App.TestSupport;

/// <summary>
/// Runs a test action just before the service saves its own records in <c>CoreDbContext</c> — after a handler's checks
/// and before its save reaches the database — for scenarios set "after the call checks X and before it is saved". The
/// action runs for the next <c>times</c> saves only, then the seam is off again; an action that throws makes that save
/// fail with its exception. The idempotency store keeps its records through its own context, so it is not affected.
/// </summary>
public sealed class BeforeSaveInterceptor : SaveChangesInterceptor
{
    private readonly Lock _gate = new();
    private Func<Task>? _action;
    private int _remaining;

    /// <summary>Runs <paramref name="action"/> before each of the next <paramref name="times"/> saves.</summary>
    public void Run(Func<Task> action, int times = 1)
    {
        lock (_gate)
        {
            _action = action;
            _remaining = times;
        }
    }

    /// <summary>Turns the seam off.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _action = null;
            _remaining = 0;
        }
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Take()?.Invoke().GetAwaiter().GetResult();
        return result;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (Take() is { } action)
        {
            await action();
        }

        return result;
    }

    /// <summary>The action for this save, counted off before it runs so a save the action itself causes is not caught.</summary>
    private Func<Task>? Take()
    {
        lock (_gate)
        {
            if (_action is null || _remaining <= 0)
            {
                return null;
            }

            var action = _action;
            if (--_remaining == 0)
            {
                _action = null;
            }

            return action;
        }
    }
}
