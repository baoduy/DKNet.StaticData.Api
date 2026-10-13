using DKNet.StaticData.App.Tests.Files.Support;
using DKNet.StaticData.App.Tests.SettingGroups.Support;
using DKNet.StaticData.App.Tests.UiSettings.Support;
using DKNet.StaticData.App.TestSupport;

namespace DKNet.StaticData.App.Tests.UiSettings;

/// <summary>
/// Spec DRK-2245 §5 "Callers and roles": every new <c>GET</c> needs <c>staticdata.read</c>, every new <c>POST</c>,
/// <c>PUT</c> and <c>DELETE</c> needs <c>staticdata.write</c>; a call with no valid token answers 401; the role is
/// checked before every input check.
/// </summary>
public sealed class SettingRolesTests(FilesHost hosts) : IClassFixture<FilesHost>
{
    public static IEnumerable<object[]> RefusedCallers => Databases.With(
        ["report-svc", "create the setting \"orders.list.table\""],
        ["report-svc", "update \"customers.list.table\""],
        ["report-svc", "delete \"customers.list.table\""],
        ["report-svc", "erase all settings"],
        ["report-svc", "create the group \"Order screens\""],
        ["report-svc", "rename \"Customer screens\" to \"Main screens\""],
        ["report-svc", "delete \"Customer screens\""],
        ["ingest-svc", "list the settings"],
        ["ingest-svc", "read \"customers.list.table\""],
        ["ingest-svc", "list the groups"],
        ["ingest-svc", "read \"Customer screens\""]);

    /// <summary>Scenario Outline: A UI settings route refuses a caller without the role it needs.</summary>
    [Theory]
    [MemberData(nameof(RefusedCallers))]
    public async Task AUiSettingsRouteRefusesACallerWithoutTheRoleItNeeds(TestDatabase database, string caller, string action)
    {
        var api = await hosts.OnAsync(database);
        var groupId = await api.HasSettingGroupAsync("backoffice-web", "Customer screens");
        var settingId = await api.HasSettingAsync("backoffice-web", "customers.list.table", groupId);
        var token = caller == "report-svc" ? Callers.ReportSvc : Callers.IngestSvc;

        using var response = action switch
        {
            "create the setting \"orders.list.table\"" => await api.CreateSettingAsync(
                token, "backoffice-web", UiSettingsApi.SettingBody("orders.list.table")),
            "update \"customers.list.table\"" => await api.UpdateSettingAsync(
                token, "backoffice-web", settingId, UiSettingsApi.SettingBody(null, value: """{"columns":["name"]}"""), "\"1\""),
            "delete \"customers.list.table\"" => await api.DeleteSettingAsync(token, "backoffice-web", settingId),
            "erase all settings" => await api.EraseSettingsAsync(token, "backoffice-web"),
            "create the group \"Order screens\"" => await api.CreateSettingGroupAsync(
                token, "backoffice-web", SettingGroupsApi.GroupBody("Order screens")),
            "rename \"Customer screens\" to \"Main screens\"" => await api.UpdateSettingGroupAsync(
                token, "backoffice-web", groupId, SettingGroupsApi.GroupBody("Main screens"), "\"1\""),
            "delete \"Customer screens\"" => await api.DeleteSettingGroupAsync(token, "backoffice-web", groupId),
            "list the settings" => await api.ListSettingsAsync(token, "backoffice-web"),
            "read \"customers.list.table\"" => await api.ReadSettingAsync(token, "backoffice-web", settingId),
            "list the groups" => await api.ListSettingGroupsAsync(token, "backoffice-web"),
            "read \"Customer screens\"" => await api.ReadSettingGroupAsync(token, "backoffice-web", groupId),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        };

        await response.ShouldBeProblemDetailsAsync(HttpStatusCode.Forbidden);
        // the setting and the group are unchanged
        (await api.SettingKeysAsync("backoffice-web")).ShouldBe(["customers.list.table"]);
        (await api.SettingGroupNamesAsync("backoffice-web")).ShouldBe(["Customer screens"]);
        var setting = await api.SettingAsync("backoffice-web", settingId);
        setting.GetProperty("version").GetInt32().ShouldBe(1);
        setting.GetProperty("groupId").GetGuid().ShouldBe(groupId);
        setting.ValueText().ShouldBe(UiSettingsApi.TableLayout);
        var group = await api.SettingGroupAsync("backoffice-web", groupId);
        group.GetProperty("name").GetString().ShouldBe("Customer screens");
        group.GetProperty("version").GetInt32().ShouldBe(1);
    }

    public static IEnumerable<object[]> InvalidTokens => Databases.With(
        ["no token", "list the settings"],
        ["no token", "erase all settings"],
        ["a token with no caller id", "create the group \"Customer screens\""]);

    /// <summary>Scenario Outline: A UI settings route refuses a call with no valid token.</summary>
    [Theory]
    [MemberData(nameof(InvalidTokens))]
    public async Task AUiSettingsRouteRefusesACallWithNoValidToken(TestDatabase database, string token, string action)
    {
        var api = await hosts.OnAsync(database);
        // "nothing changes" is checked against a setting the owner already has
        var settingId = await api.HasSettingAsync("backoffice-web", "customers.list.table");
        var bearer = token == "no token"
            ? null
            : TestTokens.Mint(new Dictionary<string, object> { ["roles"] = new[] { Callers.Read, Callers.Write } });

        using var response = action switch
        {
            "list the settings" => await api.ListSettingsAsync(bearer, "backoffice-web"),
            "erase all settings" => await api.EraseSettingsAsync(bearer, "backoffice-web"),
            "create the group \"Customer screens\"" => await api.CreateSettingGroupAsync(
                bearer, "backoffice-web", SettingGroupsApi.GroupBody("Customer screens")),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        };

        await response.ShouldHaveStatusAsync(HttpStatusCode.Unauthorized);
        (await api.SettingKeysAsync("backoffice-web")).ShouldBe(["customers.list.table"]);
        (await api.SettingAsync("backoffice-web", settingId)).GetProperty("version").GetInt32().ShouldBe(1);
        (await api.SettingGroupNamesAsync("backoffice-web")).ShouldBeEmpty();
    }

    /// <summary>Scenario: The role check comes before every input check on a setting create.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task TheRoleCheckComesBeforeEveryInputCheckOnASettingCreate(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);

        // no owner, no setting key and no value
        using var response = await api.CreateSettingAsync(Callers.ReportSvc, owner: null, "{}");

        await response.ShouldBeProblemDetailsAsync(HttpStatusCode.Forbidden);
    }
}
