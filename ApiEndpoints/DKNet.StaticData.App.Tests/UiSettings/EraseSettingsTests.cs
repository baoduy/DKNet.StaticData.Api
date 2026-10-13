using DKNet.StaticData.App.Tests.FileGroups.Support;
using DKNet.StaticData.App.Tests.Files.Support;
using DKNet.StaticData.App.Tests.SettingGroups.Support;
using DKNet.StaticData.App.Tests.UiSettings.Support;
using DKNet.StaticData.App.TestSupport;

namespace DKNet.StaticData.App.Tests.UiSettings;

/// <summary>
/// Spec DRK-2245 §5 "Erase an owner's settings": <c>DELETE /v1/ui-settings/all</c> deletes every setting, then every
/// setting group, of 1 owner, all or nothing, and leaves files, file groups and other owners untouched.
/// </summary>
public sealed class EraseSettingsTests(FilesHost hosts) : IClassFixture<FilesHost>
{
    /// <summary>Scenario Outline: The erase removes every setting and setting group of 1 owner only.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task TheEraseRemovesEverySettingAndSettingGroupOf1OwnerOnly(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var customerScreens = await api.HasSettingGroupAsync("backoffice-web", "Customer screens");
        await api.HasSettingAsync("backoffice-web", "customers.list.table", customerScreens);
        await api.HasSettingAsync("backoffice-web", "orders.list.table");
        await api.HasSettingGroupAsync("backoffice-web", "Order screens");
        await api.HasFileAsync("backoffice-web", "passport.pdf");
        await api.HasGroupAsync("backoffice-web", "Onboarding pack");
        var adminScreens = await api.HasSettingGroupAsync("backoffice-admin", "Admin screens");
        var adminSetting = await api.HasSettingAsync("backoffice-admin", "customers.list.table", adminScreens);
        (await api.SettingKeysAsync("backoffice-web")).ShouldBe(["orders.list.table", "customers.list.table"]);
        (await api.SettingGroupNamesAsync("backoffice-web")).ShouldBe(["Order screens", "Customer screens"]);

        using var response = await api.EraseSettingsAsync(Callers.LayoutSvc, "backoffice-web");

        await response.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        (await api.SettingKeysAsync("backoffice-web")).ShouldBeEmpty();
        (await api.SettingGroupNamesAsync("backoffice-web")).ShouldBeEmpty();
        (await api.FileNamesAsync("backoffice-web")).ShouldBe(["passport.pdf"]);
        (await api.GroupNamesAsync("backoffice-web")).ShouldBe(["Onboarding pack"]);
        (await api.SettingKeysAsync("backoffice-admin")).ShouldBe(["customers.list.table"]);
        (await api.SettingGroupNamesAsync("backoffice-admin")).ShouldBe(["Admin screens"]);
        (await api.SettingAsync("backoffice-admin", adminSetting)).GetProperty("groupId").GetGuid().ShouldBe(adminScreens);
    }

    /// <summary>Scenario: Erasing an owner with no setting succeeds.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task ErasingAnOwnerWithNoSettingSucceeds(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        (await api.SettingKeysAsync("backoffice-web")).ShouldBeEmpty();
        (await api.SettingGroupNamesAsync("backoffice-web")).ShouldBeEmpty();

        using var response = await api.EraseSettingsAsync(Callers.LayoutSvc, "backoffice-web");

        await response.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
    }

    /// <summary>Scenario Outline: A setting saved into a group during the erase makes the erase change nothing.</summary>
    /// <remarks>
    /// The other call is the setting create through the service's own route, sent just before the erase's next save, so
    /// the erase must reach its group delete through a save (<see cref="FilesApiFactory.BeforeSave"/>) after it has read
    /// the settings it deletes.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task ASettingSavedIntoAGroupDuringTheEraseMakesTheEraseChangeNothing(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        await api.HasSettingAsync("backoffice-web", "orders.list.table");
        var customerScreens = await api.HasSettingGroupAsync("backoffice-web", "Customer screens");
        var settingSaved = SaveRaces.DuringTheNextSave(api, () => api.CreateSettingAsync(Callers.LayoutSvc, "backoffice-web",
            UiSettingsApi.SettingBody("customers.list.table", groupId: UiSettingsApi.Id(customerScreens))));

        using var response = await api.EraseSettingsAsync(Callers.LayoutSvc, "backoffice-web");

        await response.ShouldBeProblemDetailsAsync(HttpStatusCode.Conflict);
        using (var saved = await settingSaved)
        {
            await saved.ShouldHaveStatusAsync(HttpStatusCode.Created);
        }

        (await api.SettingKeysAsync("backoffice-web")).ShouldBe(["customers.list.table", "orders.list.table"]);
        (await api.SettingGroupNamesAsync("backoffice-web")).ShouldBe(["Customer screens"]);
    }

    /// <summary>Scenario: The erase needs an owner.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task TheEraseNeedsAnOwner(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);

        using var response = await api.EraseSettingsAsync(Callers.LayoutSvc, owner: null);

        await response.ShouldBeProblemDetailsAsync(HttpStatusCode.BadRequest);
    }
}
