using DKNet.StaticData.App.Tests.Files.Support;
using DKNet.StaticData.App.Tests.SettingGroups.Support;
using DKNet.StaticData.App.TestSupport;

namespace DKNet.StaticData.App.Tests.SettingGroups;

/// <summary>
/// Spec DRK-2245 §5 "Setting groups", read and list: one group with its version tag, the owner's groups newest first,
/// paged, narrowed by optional dates; a group of another owner does not exist for the caller.
/// </summary>
public sealed class ReadAndListSettingGroupTests(FilesHost hosts) : IClassFixture<FilesHost>
{
    /// <summary>Scenario Outline: A group list shows the newest groups first, one page at a time.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task AGroupListShowsTheNewestGroupsFirstOnePageAtATime(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        await api.HasSettingGroupAsync("backoffice-web", "Group A");
        await api.HasSettingGroupAsync("backoffice-web", "Group B");
        await api.HasSettingGroupAsync("backoffice-web", "Group C");

        using var response = await api.ListSettingGroupsAsync(Callers.ReportSvc, "backoffice-web", "&pageNumber=1&pageSize=2");

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        SettingGroupsApi.Names(await response.JsonAsync()).ShouldBe(["Group C", "Group B"]);
    }

    /// <summary>Scenario: A caller reads one setting group.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task ACallerReadsOneSettingGroup(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var groupId = await api.HasSettingGroupAsync("backoffice-web", "Customer screens");
        await api.RaiseSettingGroupToVersion2Async("backoffice-web", groupId, "Customer screens");

        using var response = await api.ReadSettingGroupAsync(Callers.ReportSvc, "backoffice-web", groupId);

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        response.Headers.ETag.ShouldNotBeNull("the answer carries no ETag").Tag.ShouldBe("\"2\"");
        var group = await response.JsonAsync();
        group.GetProperty("groupId").GetGuid().ShouldBe(groupId);
        group.GetProperty("name").GetString().ShouldBe("Customer screens");
        group.GetProperty("owner").GetString().ShouldBe("backoffice-web");
        group.GetProperty("version").GetInt32().ShouldBe(2);
    }

    /// <summary>Scenario: From and to dates narrow a group list to groups created or updated in that range.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task FromAndToDatesNarrowAGroupListToGroupsCreatedOrUpdatedInThatRange(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var march = await api.HasSettingGroupAsync("backoffice-web", "March screens");
        var june = await api.HasSettingGroupAsync("backoffice-web", "June screens");
        var moved = await api.HasSettingGroupAsync("backoffice-web", "Moved screens");
        var march10 = new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero);
        await StoredFileRows.SetActivityAsync(api, "SettingGroup", march, march10, updatedOn: null);
        await StoredFileRows.SetActivityAsync(api, "SettingGroup", june, new DateTimeOffset(2026, 6, 10, 9, 0, 0, TimeSpan.Zero), updatedOn: null);
        await StoredFileRows.SetActivityAsync(api, "SettingGroup", moved, march10, new DateTimeOffset(2026, 6, 15, 9, 0, 0, TimeSpan.Zero));

        using var response = await api.ListSettingGroupsAsync(Callers.ReportSvc, "backoffice-web", "&fromDate=2026-06-01&toDate=2026-06-30");

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        SettingGroupsApi.Names(await response.JsonAsync()).ShouldBe(["June screens", "Moved screens"], ignoreOrder: true);
    }

    public static IEnumerable<object[]> OtherOwnerActions => Databases.With(
        ["read \"Customer screens\""],
        ["rename \"Customer screens\" to \"Main screens\""],
        ["delete \"Customer screens\""]);

    /// <summary>Scenario Outline: A group of another owner does not exist for the caller.</summary>
    [Theory]
    [MemberData(nameof(OtherOwnerActions))]
    public async Task AGroupOfAnotherOwnerDoesNotExistForTheCaller(TestDatabase database, string action)
    {
        var api = await hosts.OnAsync(database);
        var groupId = await api.HasSettingGroupAsync("backoffice-web", "Customer screens");

        using var response = action switch
        {
            "read \"Customer screens\"" => await api.ReadSettingGroupAsync(Callers.LayoutSvc, "backoffice-admin", groupId),
            // with the group's current version tag, so the answer is the record check, not the version tag check
            "rename \"Customer screens\" to \"Main screens\"" => await api.UpdateSettingGroupAsync(
                Callers.LayoutSvc, "backoffice-admin", groupId, SettingGroupsApi.GroupBody("Main screens"), "\"1\""),
            "delete \"Customer screens\"" => await api.DeleteSettingGroupAsync(Callers.LayoutSvc, "backoffice-admin", groupId),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        };

        await response.ShouldBeProblemDetailsAsync(HttpStatusCode.NotFound);
        var group = await api.SettingGroupAsync("backoffice-web", groupId);
        group.GetProperty("name").GetString().ShouldBe("Customer screens");
        group.GetProperty("version").GetInt32().ShouldBe(1);
    }
}
