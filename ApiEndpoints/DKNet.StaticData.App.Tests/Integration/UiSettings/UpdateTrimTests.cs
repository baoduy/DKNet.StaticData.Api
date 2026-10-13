using DKNet.StaticData.App.Tests.Files.Support;
using DKNet.StaticData.App.Tests.SettingGroups.Support;
using DKNet.StaticData.App.Tests.UiSettings.Support;
using DKNet.StaticData.App.TestSupport;

namespace DKNet.StaticData.App.Tests.Integration.UiSettings;

/// <summary>
/// Spec DRK-2245 §3 "Text rules" on the 2 update routes, which its trim scenarios leave out: an update stores the
/// trimmed setting type, a group update the trimmed name and description.
/// </summary>
public sealed class UpdateTrimTests(FilesHost hosts) : IClassFixture<FilesHost>
{
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task ASettingUpdateStoresTheTrimmedSettingType(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var settingId = await api.HasSettingAsync("backoffice-web", "customers.list.table");

        using var response = await api.UpdateSettingAsync(Callers.LayoutSvc, "backoffice-web", settingId,
            UiSettingsApi.SettingBody(null, " CardLayout "), "\"1\"");

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        (await api.SettingAsync("backoffice-web", settingId)).GetProperty("settingType").GetString().ShouldBe("CardLayout");
    }

    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task AGroupUpdateStoresTheTrimmedNameAndDescription(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var groupId = await api.HasSettingGroupAsync("backoffice-web", "Customer screens");

        using var response = await api.UpdateSettingGroupAsync(Callers.LayoutSvc, "backoffice-web", groupId,
            SettingGroupsApi.GroupBody(" Main screens ", " Main layouts "), "\"1\"");

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        var group = await api.SettingGroupAsync("backoffice-web", groupId);
        group.GetProperty("name").GetString().ShouldBe("Main screens");
        group.GetProperty("description").GetString().ShouldBe("Main layouts");
    }
}
