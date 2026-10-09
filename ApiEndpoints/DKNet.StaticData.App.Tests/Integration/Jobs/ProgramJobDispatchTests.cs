using DKNet.StaticData.App.TestSupport;
using DKNet.StaticData.Infra.Contexts;
using DKNet.StaticData.Infra.Postgres;
using Microsoft.EntityFrameworkCore;

namespace DKNet.StaticData.App.Tests.Integration.Jobs;

/// <summary>
/// The process entry point's job dispatch (<c>Program.cs</c>): a job argument runs that job and returns its exit
/// code instead of serving. Driven through the assembly entry point with real process arguments.
/// </summary>
public class ProgramJobDispatchTests
{
    private static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(60);

    [Fact]
    public async Task UnknownJob_ReturnsOne_AndNamesTheKnownJobs()
    {
        var stderr = new StringWriter();
        var previous = Console.Error;
        Console.SetError(stderr);
        int exitCode;
        try
        {
            exitCode = await RunEntryPointAsync("nope");
        }
        finally
        {
            Console.SetError(previous);
        }

        exitCode.ShouldBe(1);
        stderr.ToString().ShouldBe($"Unrecognized job \"nope\". Known jobs: migration{Environment.NewLine}");
    }

    [Fact]
    public async Task MigrationJob_OnAnEmptyPostgresDatabase_ReturnsZero_AndLeavesNoPendingMigration()
    {
        var server = await TestDatabaseServer.SharedAsync(TestDatabase.Postgres);
        var (_, connectionString) = await server.CreateEmptyDatabaseAsync();
        await using var db = new CoreDbContext(
            new DbContextOptionsBuilder<CoreDbContext>().UsePostgres(connectionString).Options);
        (await db.Database.GetAppliedMigrationsAsync()).ShouldBeEmpty("the database was not empty before the job");

        var exitCode = await RunEntryPointAsync("migration", $"--ConnectionStrings:AppDb={connectionString}");

        exitCode.ShouldBe(0);
        (await db.Database.GetAppliedMigrationsAsync()).ShouldNotBeEmpty("the job applied no migration");
        (await db.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
    }

    /// <summary>Runs <c>Program</c>'s entry point; a run that serves instead of returning fails the test.</summary>
    private static async Task<int> RunEntryPointAsync(params string[] args)
    {
        var entryPoint = typeof(DKNet.StaticData.Api.Program).Assembly.EntryPoint!;
        return await Task.Run(() => (int)entryPoint.Invoke(null, [args])!).WaitAsync(RunTimeout);
    }
}
