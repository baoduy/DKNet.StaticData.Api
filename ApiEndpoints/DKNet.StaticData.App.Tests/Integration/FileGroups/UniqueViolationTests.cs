using System.Data.Common;
using System.Globalization;
using DKNet.StaticData.App.Tests.Files.Support;
using DKNet.StaticData.App.Tests.UiSettings.Support;
using DKNet.StaticData.App.TestSupport;
using DKNet.StaticData.AppServices.Features.FileGroups;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace DKNet.StaticData.App.Tests.Integration.FileGroups;

/// <summary>
/// Brief DRK-2246 §6a D9, on the errors each real database raises: a duplicate unique index key (Postgres 23505, SQL
/// Server 2601) and a duplicate primary key (Postgres 23505, SQL Server 2627) are unique violations; a foreign key
/// violation (23503, 547) and any other failed save are not.
/// </summary>
public sealed class UniqueViolationTests(FilesHost hosts) : IClassFixture<FilesHost>
{
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task ADuplicateSettingKeyIsAUniqueViolation(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        await api.HasSettingAsync("backoffice-web", "customers.list.table");
        var orders = await api.HasSettingAsync("backoffice-web", "orders.list.table");

        var refused = await Should.ThrowAsync<DbException>(() => SetAsync(api, "SettingKey", "customers.list.table", orders));

        Code(refused).ShouldBe(database == TestDatabase.Postgres ? "23505" : "2601");
        UniqueViolation.IsIn(new DbUpdateException("The save failed.", refused)).ShouldBeTrue();
    }

    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task ADuplicatePrimaryKeyIsAUniqueViolation(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var customers = await api.HasSettingAsync("backoffice-web", "customers.list.table");
        var orders = await api.HasSettingAsync("backoffice-web", "orders.list.table");

        var refused = await Should.ThrowAsync<DbException>(() => SetAsync(api, "Id", customers, orders));

        Code(refused).ShouldBe(database == TestDatabase.Postgres ? "23505" : "2627");
        UniqueViolation.IsIn(new DbUpdateException("The save failed.", refused)).ShouldBeTrue();
    }

    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task AForeignKeyViolationIsNotAUniqueViolation(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var settingId = await api.HasSettingAsync("backoffice-web", "customers.list.table");

        var refused = await Should.ThrowAsync<DbException>(() => SetAsync(api, "GroupId", Guid.NewGuid(), settingId));

        Code(refused).ShouldBe(database == TestDatabase.Postgres ? "23503" : "547");
        UniqueViolation.IsIn(new DbUpdateException("The save failed.", refused)).ShouldBeFalse();
    }

    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task ASettingWithNoKeyIsAnotherFailedSave(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var settingId = await api.HasSettingAsync("backoffice-web", "customers.list.table");

        var refused = await Should.ThrowAsync<DbException>(() => SetAsync(api, "SettingKey", DBNull.Value, settingId));

        UniqueViolation.IsIn(new DbUpdateException("The save failed.", refused)).ShouldBeFalse();
    }

    [Fact]
    public void ASaveWithNoDatabaseErrorIsNotAUniqueViolation()
    {
        UniqueViolation.IsIn(new DbUpdateException("The save failed.")).ShouldBeFalse();
        UniqueViolation.IsIn(new DbUpdateException("The save failed.", new InvalidOperationException())).ShouldBeFalse();
    }

    /// <summary>The database's own error code: Postgres SQLSTATE, SQL Server error number.</summary>
    private static string? Code(DbException refused) =>
        refused is SqlException sql ? sql.Number.ToString(CultureInfo.InvariantCulture) : refused.SqlState;

    private static Task<int> SetAsync(FilesApi api, string column, object value, Guid settingId)
    {
        var s = api.Server;
        return s.ExecuteAsync(
            api.Host.ConnectionString,
            $"UPDATE {s.Quote("settings")}.{s.Quote("UiSettings")} SET {s.Quote(column)} = @value WHERE {s.Quote("Id")} = @id",
            ("value", value), ("id", settingId));
    }
}
