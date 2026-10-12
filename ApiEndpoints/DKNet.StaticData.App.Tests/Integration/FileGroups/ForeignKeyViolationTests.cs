using System.Data.Common;
using DKNet.StaticData.App.Tests.Files.Support;
using DKNet.StaticData.App.TestSupport;
using DKNet.StaticData.AppServices.Features.FileGroups;
using Microsoft.EntityFrameworkCore;

namespace DKNet.StaticData.App.Tests.Integration.FileGroups;

/// <summary>
/// Brief DRK-2239 §6a D5, on the errors each real database raises: a file linked to no group is a foreign key violation
/// (Postgres 23503, SQL Server 547); a file with no name is another failed save; a save with no database error is not one.
/// </summary>
public sealed class ForeignKeyViolationTests(FilesHost hosts) : IClassFixture<FilesHost>
{
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task AFileLinkedToNoGroupIsAForeignKeyViolation(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var fileId = await api.HasFileAsync("customer-1", "passport.pdf");

        var refused = await Should.ThrowAsync<DbException>(() => SetAsync(api, "GroupId", Guid.NewGuid(), fileId));

        ForeignKeyViolation.IsIn(new DbUpdateException("The save failed.", refused)).ShouldBeTrue();
    }

    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task AFileWithNoNameIsAnotherFailedSave(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var fileId = await api.HasFileAsync("customer-1", "passport.pdf");

        var refused = await Should.ThrowAsync<DbException>(() => SetAsync(api, "FileName", DBNull.Value, fileId));

        ForeignKeyViolation.IsIn(new DbUpdateException("The save failed.", refused)).ShouldBeFalse();
    }

    [Fact]
    public void ASaveWithNoDatabaseErrorIsNotAForeignKeyViolation()
    {
        ForeignKeyViolation.IsIn(new DbUpdateException("The save failed.")).ShouldBeFalse();
        ForeignKeyViolation.IsIn(new DbUpdateException("The save failed.", new InvalidOperationException())).ShouldBeFalse();
    }

    private static Task<int> SetAsync(FilesApi api, string column, object value, Guid fileId)
    {
        var s = api.Server;
        return s.ExecuteAsync(
            api.Host.ConnectionString,
            $"UPDATE {s.Quote("files")}.{s.Quote("StoredFiles")} SET {s.Quote(column)} = @value WHERE {s.Quote("Id")} = @id",
            ("value", value), ("id", fileId));
    }
}
