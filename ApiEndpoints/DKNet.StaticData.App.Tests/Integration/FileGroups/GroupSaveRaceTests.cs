using DKNet.StaticData.App.Tests.FileGroups.Support;
using DKNet.StaticData.App.Tests.Files.Support;
using DKNet.StaticData.App.TestSupport;

namespace DKNet.StaticData.App.Tests.Integration.FileGroups;

/// <summary>
/// Brief DRK-2239 §3 rows 14 and 15: a linked file stops a group delete before its save, and what another call changes
/// between a group route's checks and its save, made in the database just before the route's own save. An update never
/// overwrites another call's change (spec: of 2 updates with the same version, only the first is kept), whatever the
/// timing of the 2 calls.
/// </summary>
public sealed class GroupSaveRaceTests(FilesHost hosts) : IClassFixture<FilesHost>
{
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task AFileLinkedAfterTheCheckMakesTheGroupDeleteAnswer409(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var groupId = await api.HasGroupAsync("customer-1", "Onboarding pack");
        var fileId = await api.HasFileAsync("customer-1", "passport.pdf");
        api.Host.BeforeSave.Run(() => SetAsync(api, "StoredFiles", "GroupId", groupId, fileId));

        using var response = await api.DeleteGroupAsync(Callers.OnboardingSvc, "customer-1", groupId);

        await response.ShouldBeProblemDetailsAsync(HttpStatusCode.Conflict);
        (await api.GroupNamesAsync("customer-1")).ShouldBe(["Onboarding pack"]);
        (await api.FileAsync("customer-1", fileId)).GetProperty("version").GetInt32().ShouldBe(1);
    }

    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task AGroupWithALinkedFileAnswers409BeforeItsSave(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var groupId = await api.HasGroupAsync("customer-1", "Onboarding pack");
        var fileId = await api.HasFileAsync("customer-1", "passport.pdf");
        await SetAsync(api, "StoredFiles", "GroupId", groupId, fileId);
        api.Host.BeforeSave.Run(() => throw new InvalidOperationException("the delete must not reach its save"));

        using var response = await api.DeleteGroupAsync(Callers.OnboardingSvc, "customer-1", groupId);

        await response.ShouldBeProblemDetailsAsync(HttpStatusCode.Conflict);
        (await api.GroupNamesAsync("customer-1")).ShouldBe(["Onboarding pack"]);
    }

    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task AGroupDeletedAfterTheCheckStillAnswers204(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var groupId = await api.HasGroupAsync("customer-1", "Onboarding pack");
        (await api.GroupNamesAsync("customer-1")).ShouldBe(["Onboarding pack"]);
        api.Host.BeforeSave.Run(() => DeleteGroupRowAsync(api, groupId));

        using var response = await api.DeleteGroupAsync(Callers.OnboardingSvc, "customer-1", groupId);

        await response.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        (await api.GroupNamesAsync("customer-1")).ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task AGroupUpdatedAfterTheVersionCheckMakesTheUpdateAnswer412(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var groupId = await api.HasGroupAsync("customer-1", "Onboarding pack");
        api.Host.BeforeSave.Run(() => SetAsync(api, "FileGroups", "Version", 2, groupId));

        using var response = await api.UpdateGroupAsync(
            Callers.OnboardingSvc, "customer-1", groupId, FileGroupsApi.GroupBody("Pack 2"), "\"1\"");

        await response.ShouldBeProblemDetailsAsync(HttpStatusCode.PreconditionFailed);
        var group = await api.GroupAsync("customer-1", groupId);
        group.GetProperty("name").GetString().ShouldBe("Onboarding pack");
        group.GetProperty("version").GetInt32().ShouldBe(2);
    }

    private static async Task SetAsync(FilesApi api, string table, string column, object value, Guid id)
    {
        var s = api.Server;
        (await s.ExecuteAsync(
            api.Host.ConnectionString,
            $"UPDATE {s.Quote("files")}.{s.Quote(table)} SET {s.Quote(column)} = @value WHERE {s.Quote("Id")} = @id",
            ("value", value), ("id", id))).ShouldBe(1);
    }

    private static async Task DeleteGroupRowAsync(FilesApi api, Guid groupId)
    {
        var s = api.Server;
        (await s.ExecuteAsync(
            api.Host.ConnectionString,
            $"DELETE FROM {s.Quote("files")}.{s.Quote("FileGroups")} WHERE {s.Quote("Id")} = @id",
            ("id", groupId))).ShouldBe(1);
    }
}
