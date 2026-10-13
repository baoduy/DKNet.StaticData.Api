using DKNet.StaticData.App.Tests.Files.Support;
using DKNet.StaticData.App.Tests.SettingGroups.Support;
using DKNet.StaticData.App.Tests.UiSettings.Support;
using DKNet.StaticData.App.TestSupport;

namespace DKNet.StaticData.App.Tests.SettingGroups;

/// <summary>
/// Spec DRK-2245 §5 "Setting groups", delete: a group with no linked setting is deleted with 204 and a version tag is
/// ignored; a group with a linked setting answers 409 and changes nothing; removing the last linked setting lets the
/// group be deleted. Races: a group deleted during a setting create makes the create answer 404, and a setting saved
/// first makes the group delete answer 409.
/// </summary>
public sealed class DeleteSettingGroupTests(FilesHost hosts) : IClassFixture<FilesHost>
{
    /// <summary>Scenario: A caller deletes a group with no linked setting.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task ACallerDeletesAGroupWithNoLinkedSetting(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var groupId = await api.HasSettingGroupAsync("backoffice-web", "Customer screens");
        (await api.SettingGroupNamesAsync("backoffice-web")).ShouldBe(["Customer screens"]);

        using var response = await api.DeleteSettingGroupAsync(Callers.LayoutSvc, "backoffice-web", groupId, ifMatch: "\"7\"");

        await response.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        using var read = await api.ReadSettingGroupAsync(Callers.LayoutSvc, "backoffice-web", groupId);
        await read.ShouldBeProblemDetailsAsync(HttpStatusCode.NotFound);
        (await api.SettingGroupNamesAsync("backoffice-web")).ShouldBeEmpty();
    }

    /// <summary>Scenario Outline: A group with a linked setting is not deleted.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task AGroupWithALinkedSettingIsNotDeleted(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var groupId = await api.HasSettingGroupAsync("backoffice-web", "Customer screens");
        var settingId = await api.HasSettingAsync("backoffice-web", "customers.list.table", groupId);

        using var response = await api.DeleteSettingGroupAsync(Callers.LayoutSvc, "backoffice-web", groupId);

        await response.ShouldBeProblemDetailsAsync(HttpStatusCode.Conflict);
        (await api.SettingGroupAsync("backoffice-web", groupId)).GetProperty("version").GetInt32().ShouldBe(1);
        (await api.SettingAsync("backoffice-web", settingId)).GetProperty("groupId").GetGuid().ShouldBe(groupId);
    }

    public static IEnumerable<object[]> Removals => Databases.With(
        ["deleted \"customers.list.table\""],
        ["updated \"customers.list.table\" with no group"]);

    /// <summary>Scenario Outline: Removing the last linked setting lets the group be deleted.</summary>
    [Theory]
    [MemberData(nameof(Removals))]
    public async Task RemovingTheLastLinkedSettingLetsTheGroupBeDeleted(TestDatabase database, string removal)
    {
        var api = await hosts.OnAsync(database);
        var groupId = await api.HasSettingGroupAsync("backoffice-web", "Customer screens");
        var settingId = await api.HasSettingAsync("backoffice-web", "customers.list.table", groupId);
        using (var removed = removal == "deleted \"customers.list.table\""
                   ? await api.DeleteSettingAsync(Callers.LayoutSvc, "backoffice-web", settingId)
                   : await api.UpdateSettingAsync(Callers.LayoutSvc, "backoffice-web", settingId,
                       UiSettingsApi.SettingBody(null), "\"1\""))
        {
            await removed.ShouldHaveStatusAsync(removal == "deleted \"customers.list.table\"" ? HttpStatusCode.NoContent : HttpStatusCode.OK);
        }

        using var response = await api.DeleteSettingGroupAsync(Callers.LayoutSvc, "backoffice-web", groupId);

        await response.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        (await api.SettingGroupNamesAsync("backoffice-web")).ShouldBeEmpty();
    }

    /// <summary>Scenario: A group deleted during a setting create makes the create fail.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task AGroupDeletedDuringASettingCreateMakesTheCreateFail(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var groupId = await api.HasSettingGroupAsync("backoffice-web", "Customer screens");
        // the group is deleted after the create checks it and before the setting is saved
        var groupDeleted = SaveRaces.DuringTheNextSave(api,
            () => api.DeleteSettingGroupAsync(Callers.LayoutSvc, "backoffice-web", groupId));

        using var response = await api.CreateSettingAsync(Callers.LayoutSvc, "backoffice-web",
            UiSettingsApi.SettingBody("customers.list.table", groupId: UiSettingsApi.Id(groupId)));

        await response.ShouldBeProblemDetailsAsync(HttpStatusCode.NotFound);
        using (var deleted = await groupDeleted)
        {
            await deleted.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        }

        (await api.SettingKeysAsync("backoffice-web")).ShouldBeEmpty();
    }

    /// <summary>Scenario: A setting saved first makes the group delete fail.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task ASettingSavedFirstMakesTheGroupDeleteFail(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var groupId = await api.HasSettingGroupAsync("backoffice-web", "Customer screens");
        // the setting is saved into the group after the delete checks for linked settings and before the delete is saved
        var settingSaved = SaveRaces.DuringTheNextSave(api, () => api.CreateSettingAsync(Callers.LayoutSvc, "backoffice-web",
            UiSettingsApi.SettingBody("customers.list.table", groupId: UiSettingsApi.Id(groupId))));

        using var response = await api.DeleteSettingGroupAsync(Callers.LayoutSvc, "backoffice-web", groupId);

        await response.ShouldBeProblemDetailsAsync(HttpStatusCode.Conflict);
        Guid settingId;
        using (var saved = await settingSaved)
        {
            await saved.ShouldHaveStatusAsync(HttpStatusCode.Created);
            settingId = (await saved.JsonAsync()).GetProperty("settingId").GetGuid();
        }

        (await api.SettingGroupNamesAsync("backoffice-web")).ShouldBe(["Customer screens"]);
        (await api.SettingAsync("backoffice-web", settingId)).GetProperty("groupId").GetGuid().ShouldBe(groupId);
    }
}
