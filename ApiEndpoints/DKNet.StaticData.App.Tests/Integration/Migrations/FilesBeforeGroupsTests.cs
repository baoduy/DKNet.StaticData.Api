using DKNet.StaticData.App.Tests.FileGroups.Support;
using DKNet.StaticData.App.Tests.Files.Support;
using DKNet.StaticData.App.TestSupport;
using DKNet.StaticData.Infra.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DKNet.StaticData.App.Tests.Integration.Migrations;

/// <summary>
/// Spec DRK-2235 §5 "Files and groups": files uploaded before this slice have no group after the migration. A database
/// is brought to the slice 2 tables (each database's <c>AddStoredFile</c> migration), a file is stored there with the
/// slice 2 columns, and the service's start-up migration then brings the database up to date.
/// </summary>
public sealed class FilesBeforeGroupsTests : IAsyncLifetime
{
    private readonly string _blobRoot = Path.Combine(Path.GetTempPath(), "staticdata-files-tests", Guid.NewGuid().ToString("N"));

    public static TheoryData<TestDatabase, string> Slice2Migrations => new()
    {
        { TestDatabase.Postgres, "20261010095146_AddStoredFile" },
        { TestDatabase.SqlServer, "20261010095148_AddStoredFile" }
    };

    /// <summary>Scenario Outline: Files stored before groups existed have no group.</summary>
    [Theory]
    [MemberData(nameof(Slice2Migrations))]
    public async Task FilesStoredBeforeGroupsExistedHaveNoGroup(TestDatabase database, string slice2Migration)
    {
        var server = await TestDatabaseServer.SharedAsync(database);
        var (_, connectionString) = await server.CreateEmptyDatabaseAsync();
        await MigrateToAsync(database, connectionString, slice2Migration);
        var fileId = Guid.NewGuid();
        await InsertSlice2FileAsync(server, connectionString, fileId);

        // the service brings its database up to date at start
        await using var host = await FilesApiFactory.StartAsync(database, connectionString, _blobRoot);
        var details = await new FilesApi(host, server).FileAsync("customer-1", fileId);

        details.GetProperty("fileName").GetString().ShouldBe("passport.pdf");
        details.Has("groupId").ShouldBeFalse();
        details.GetProperty("version").GetInt32().ShouldBe(1);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        if (Directory.Exists(_blobRoot))
        {
            Directory.Delete(_blobRoot, recursive: true);
        }

        return Task.CompletedTask;
    }

    /// <summary>Migrates the empty database to <paramref name="migration"/> with the service's own context and migrations.</summary>
    private static async Task MigrateToAsync(TestDatabase database, string connectionString, string migration)
    {
        var (host, startupError) = await DatabaseSettingApiFactory.StartAsync(
            database == TestDatabase.Postgres ? "Postgres" : "SqlServer", connectionString, runDbMigrationWhenAppStart: false);
        startupError.ShouldBeNull();
        await using var started = host.ShouldNotBeNull();
        using (var scope = started.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
            await context.GetService<IMigrator>().MigrateAsync(migration);
            (await context.Database.GetAppliedMigrationsAsync()).Last().ShouldBe(migration);
        }
    }

    /// <summary>"Owner customer-1 has the file passport.pdf", written with the slice 2 columns of <c>files.StoredFiles</c>.</summary>
    private static async Task InsertSlice2FileAsync(TestDatabaseServer server, string connectionString, Guid fileId)
    {
        var columns = string.Join(", ", new[]
        {
            "Id", "Checksum", "ContentType", "FileName", "OwnedBy", "SizeBytes", "StorageKey", "Version", "CreatedBy", "CreatedOn"
        }.Select(server.Quote));
        var inserted = await server.ExecuteAsync(
            connectionString,
            $"INSERT INTO {server.Quote("files")}.{server.Quote("StoredFiles")} ({columns}) " +
            "VALUES (@id, @checksum, @contentType, @fileName, @owner, @size, @storageKey, 1, @createdBy, @createdOn)",
            ("id", fileId),
            ("checksum", new string('0', 64)),
            ("contentType", "application/pdf"),
            ("fileName", "passport.pdf"),
            ("owner", "customer-1"),
            ("size", 1024L),
            ("storageKey", $"files/{fileId}.pdf"),
            ("createdBy", "onboarding-svc"),
            ("createdOn", DateTimeOffset.UtcNow));
        inserted.ShouldBe(1);
    }
}
