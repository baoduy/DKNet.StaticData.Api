using System.Text.RegularExpressions;
using DKNet.StaticData.App.Tests.Files.Support;
using DKNet.StaticData.App.TestSupport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace DKNet.StaticData.App.Tests.Files;

/// <summary>
/// Spec DRK-2206 §5 "Upload", the hourly sweep: every replica deletes the expired idempotency records once an hour and
/// logs how many; a failed sweep logs a warning and the next hour tries again.
/// </summary>
/// <remarks>
/// The hour passes on the host's <see cref="TimeProvider"/>, never on the wall clock. The fake clock starts 1 hour
/// behind the wall clock. To delete records, it is set to the wall clock's "now", so both clocks agree on which records
/// have expired — whether the sweep compares <c>ExpiresAt</c> with its injected clock or with the database's own time —
/// and every seeded expiry is at least a minute from that moment. To retry, it moves exactly 1 hour, then 59 minutes,
/// then 1 minute. The service retries a database call that fails (<c>EnableRetryOnFailure</c>), so a sweep against a
/// stopped database may take minutes to give up.
/// </remarks>
public sealed class IdempotencySweepTests(FilesHost hosts) : IClassFixture<FilesHost>
{
    private static readonly TimeSpan SweepWait = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan FailedSweepWait = TimeSpan.FromMinutes(4);

    /// <summary>Scenario Outline: The hourly sweep deletes expired idempotency records.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task TheHourlySweepDeletesExpiredIdempotencyRecords(TestDatabase database)
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow.AddHours(-1));
        var api = await hosts.OnFreshDatabaseAsync(database, clock);
        var sweepAt = DateTimeOffset.UtcNow;
        await IdempotencyRecords.InsertAsync(api, "expired-1", sweepAt.AddMinutes(-1));
        await IdempotencyRecords.InsertAsync(api, "expired-2", sweepAt.AddMinutes(-1));
        await IdempotencyRecords.InsertAsync(api, "still-valid", sweepAt.AddHours(1));
        (await IdempotencyRecords.KeysAsync(api)).ShouldBe(["expired-1", "expired-2", "still-valid"], ignoreOrder: true);

        clock.SetUtcNow(sweepAt); // an hour or more has passed: the hourly sweep runs

        (await Eventually.IsTrueAsync(() => DeletedTwo(api), SweepWait))
            .ShouldBeTrue("no Information log entry says the sweep deleted 2 records");
        (await IdempotencyRecords.KeysAsync(api)).ShouldBe(["still-valid"]);
    }

    /// <summary>Scenario: A failed sweep is retried the next hour.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task AFailedSweepIsRetriedTheNextHour(TestDatabase database)
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow.AddHours(-1));
        var api = await hosts.OnOwnServerAsync(database, clock);
        await api.Server.StopAsync(); // the database cannot be reached
        var failuresBefore = Failures(api);

        clock.Advance(TimeSpan.FromHours(1)); // the hourly sweep runs

        (await Eventually.IsTrueAsync(() => Failures(api) == failuresBefore + 1, FailedSweepWait))
            .ShouldBeTrue("no Warning log entry with the exception type was written for the failed sweep");
        // a sweep may log its warning before it schedules the next run; let it schedule before the clock moves on
        await Task.Delay(TimeSpan.FromSeconds(1));

        clock.Advance(TimeSpan.FromMinutes(59));
        (await Eventually.IsTrueAsync(() => Failures(api) > failuresBefore + 1, TimeSpan.FromSeconds(2)))
            .ShouldBeFalse("the sweep ran again before the hour was over");

        clock.Advance(TimeSpan.FromMinutes(1)); // the next sweep runs 1 hour later
        (await Eventually.IsTrueAsync(() => Failures(api) == failuresBefore + 2, FailedSweepWait))
            .ShouldBeTrue("the sweep did not run again 1 hour after the failed one");
    }

    private static bool DeletedTwo(FilesApi api) =>
        api.Host.LogCapture.Entries.Any(e =>
            e.Level == LogLevel.Information &&
            e.Message.Contains("deleted", StringComparison.OrdinalIgnoreCase) &&
            e.Values.Any(v => v.Value is int or long && Convert.ToInt64(v.Value) == 2));

    /// <summary>Warning entries of a failed sweep: each names the exception type.</summary>
    private static int Failures(FilesApi api) =>
        api.Host.LogCapture.Entries.Count(e =>
            e.Level == LogLevel.Warning &&
            !e.Category.StartsWith("Microsoft.", StringComparison.Ordinal) &&
            e.Texts().Any(t => ExceptionType().IsMatch(t)) &&
            e.Texts().Any(t => t.Contains("sweep", StringComparison.OrdinalIgnoreCase) ||
                               t.Contains("idempotency", StringComparison.OrdinalIgnoreCase)));

    private static Regex ExceptionType() => new(@"\b\w+Exception\b", RegexOptions.None, TimeSpan.FromSeconds(1));
}
