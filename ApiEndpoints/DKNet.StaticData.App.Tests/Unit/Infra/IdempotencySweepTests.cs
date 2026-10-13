using DKNet.StaticData.App.TestSupport;
using DKNet.StaticData.Infra.Contexts;
using DKNet.StaticData.Infra.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DKNet.StaticData.App.Tests.Unit.Infra;

/// <summary>A sweep cut short because the host is stopping is not a failed sweep: it logs no warning.</summary>
public class IdempotencySweepTests
{
    [Fact]
    public async Task ASweepCancelledByTheHostStoppingLogsNoWarning()
    {
        var logs = new TestLogCapture();
        await using var services = new ServiceCollection()
            .AddLogging(logging => logging.AddProvider(logs))
            .AddDbContext<CoreDbContext>(o => o.UseNpgsql("Host=127.0.0.1;Port=1;Database=none;Username=none"))
            .BuildServiceProvider();
        var sweep = new IdempotencySweep(
            services.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            services.GetRequiredService<ILogger<IdempotencySweep>>());
        using var stopping = new CancellationTokenSource();
        await stopping.CancelAsync();

        await sweep.SweepAsync(stopping.Token);

        logs.Entries.ShouldNotContain(e => e.Level >= LogLevel.Warning);
    }
}
