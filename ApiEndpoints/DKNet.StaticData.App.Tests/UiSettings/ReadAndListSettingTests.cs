using DKNet.StaticData.App.Tests.Files.Support;
using DKNet.StaticData.App.Tests.SettingGroups.Support;
using DKNet.StaticData.App.Tests.UiSettings.Support;
using DKNet.StaticData.App.TestSupport;

namespace DKNet.StaticData.App.Tests.UiSettings;

/// <summary>
/// Spec DRK-2245 §5 "Read and list settings": one setting with its version tag, the owner's settings newest first,
/// paged, values included, narrowed by exact setting key and group filters and by optional dates; a setting of another
/// owner does not exist for the caller.
/// </summary>
public sealed class ReadAndListSettingTests(FilesHost hosts) : IClassFixture<FilesHost>
{
    /// <summary>Scenario: A caller reads one setting.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task ACallerReadsOneSetting(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var settingId = await api.HasSettingAsync("backoffice-web", "customers.list.table");
        await api.RaiseSettingToVersion2Async("backoffice-web", settingId);

        using var response = await api.ReadSettingAsync(Callers.ReportSvc, "backoffice-web", settingId);

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        response.Headers.ETag?.Tag.ShouldBe("\"2\"");
        var setting = await response.JsonAsync();
        setting.GetProperty("settingId").GetGuid().ShouldBe(settingId);
        setting.GetProperty("settingKey").GetString().ShouldBe("customers.list.table");
        setting.GetProperty("settingType").GetString().ShouldBe("TableLayout");
        setting.GetProperty("owner").GetString().ShouldBe("backoffice-web");
        setting.GetProperty("version").GetInt32().ShouldBe(2);
        setting.ValueText().ShouldBe(UiSettingsApi.TableLayout);
    }

    public static IEnumerable<object[]> OtherOwnerActions => Databases.With(
        ["read \"customers.list.table\""],
        ["update \"customers.list.table\" with its version"],
        ["delete \"customers.list.table\""]);

    /// <summary>Scenario Outline: A setting of another owner does not exist for the caller.</summary>
    [Theory]
    [MemberData(nameof(OtherOwnerActions))]
    public async Task ASettingOfAnotherOwnerDoesNotExistForTheCaller(TestDatabase database, string action)
    {
        var api = await hosts.OnAsync(database);
        var settingId = await api.HasSettingAsync("backoffice-web", "customers.list.table");

        using var response = action switch
        {
            "read \"customers.list.table\"" => await api.ReadSettingAsync(Callers.LayoutSvc, "backoffice-admin", settingId),
            "update \"customers.list.table\" with its version" => await api.UpdateSettingAsync(
                Callers.LayoutSvc, "backoffice-admin", settingId, UiSettingsApi.SettingBody(null, value: """{"columns":[]}"""), "\"1\""),
            "delete \"customers.list.table\"" => await api.DeleteSettingAsync(Callers.LayoutSvc, "backoffice-admin", settingId),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        };

        await response.ShouldBeProblemDetailsAsync(HttpStatusCode.NotFound);
        var setting = await api.SettingAsync("backoffice-web", settingId);
        setting.GetProperty("version").GetInt32().ShouldBe(1);
        setting.ValueText().ShouldBe(UiSettingsApi.TableLayout);
    }

    /// <summary>Scenario Outline: A setting list shows the newest settings first, one page at a time, values included.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task ASettingListShowsTheNewestSettingsFirstOnePageAtATimeValuesIncluded(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        await api.HasSettingAsync("backoffice-web", "a.table", value: """{"table":"a"}""");
        await api.HasSettingAsync("backoffice-web", "b.table", value: """{"table":"b"}""");
        await api.HasSettingAsync("backoffice-web", "c.table", value: """{"table":"c"}""");

        using var response = await api.ListSettingsAsync(Callers.ReportSvc, "backoffice-web", "&pageNumber=1&pageSize=2");

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        var page = await response.JsonAsync();
        UiSettingsApi.SettingKeys(page).ShouldBe(["c.table", "b.table"]);
        page.GetProperty("items").EnumerateArray().Select(item => item.ValueText())
            .ShouldBe(["""{"table":"c"}""", """{"table":"b"}"""]);
    }

    public static IEnumerable<object[]> Filters => Databases.With(
        ["setting key \"customers.list.table\"", "200 holding only \"customers.list.table\""],
        ["setting key \"Customers.List.Table\"", "200 with an empty list"],
        ["the group \"Customer screens\"", "200 holding \"customers.edit.form\" then \"customers.list.table\""],
        ["setting key \"orders.list.table\" and the group \"Customer screens\"", "200 with an empty list"],
        ["a group id that names no group", "200 with an empty list"],
        ["the group \"Admin screens\"", "200 with an empty list"],
        ["the group id \"not-a-guid\"", "400 as problem details"]);

    /// <summary>Scenario Outline: The setting list filters match exactly and combine.</summary>
    [Theory]
    [MemberData(nameof(Filters))]
    public async Task TheSettingListFiltersMatchExactlyAndCombine(TestDatabase database, string filter, string answer)
    {
        var api = await hosts.OnAsync(database);
        var customerScreens = await api.HasSettingGroupAsync("backoffice-web", "Customer screens");
        await api.HasSettingAsync("backoffice-web", "customers.list.table", customerScreens);
        await api.HasSettingAsync("backoffice-web", "customers.edit.form", customerScreens);
        await api.HasSettingAsync("backoffice-web", "orders.list.table");
        var adminScreens = await api.HasSettingGroupAsync("backoffice-admin", "Admin screens");
        var query = filter switch
        {
            "setting key \"customers.list.table\"" => "&settingKey=customers.list.table",
            "setting key \"Customers.List.Table\"" => "&settingKey=Customers.List.Table",
            "the group \"Customer screens\"" => $"&groupId={customerScreens}",
            "setting key \"orders.list.table\" and the group \"Customer screens\"" =>
                $"&settingKey=orders.list.table&groupId={customerScreens}",
            "a group id that names no group" => $"&groupId={Guid.NewGuid()}",
            "the group \"Admin screens\"" => $"&groupId={adminScreens}",
            "the group id \"not-a-guid\"" => "&groupId=not-a-guid",
            _ => throw new ArgumentOutOfRangeException(nameof(filter), filter, null)
        };

        using var response = await api.ListSettingsAsync(Callers.ReportSvc, "backoffice-web", "&pageNumber=1&pageSize=100" + query);

        if (answer == "400 as problem details")
        {
            await response.ShouldBeProblemDetailsAsync(HttpStatusCode.BadRequest);
            return;
        }

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        string[] expected = answer switch
        {
            "200 holding only \"customers.list.table\"" => ["customers.list.table"],
            "200 holding \"customers.edit.form\" then \"customers.list.table\"" => ["customers.edit.form", "customers.list.table"],
            "200 with an empty list" => [],
            _ => throw new ArgumentOutOfRangeException(nameof(answer), answer, null)
        };
        UiSettingsApi.SettingKeys(await response.JsonAsync()).ShouldBe(expected);
    }

    /// <summary>Scenario Outline: A setting list with no dates returns old settings too.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task ASettingListWithNoDatesReturnsOldSettingsToo(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var settingId = await api.HasSettingAsync("backoffice-web", "customers.list.table");
        var fourMonthsAgo = DateTimeOffset.UtcNow.AddMonths(-4);
        await StoredFileRows.SetActivityAsync(api, "UiSetting", settingId, fourMonthsAgo, fourMonthsAgo);

        using var response = await api.ListSettingsAsync(Callers.ReportSvc, "backoffice-web", "&settingKey=customers.list.table");

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        UiSettingsApi.SettingKeys(await response.JsonAsync()).ShouldBe(["customers.list.table"]);
    }

    /// <summary>Scenario: From and to dates narrow a setting list to settings created or updated in that range.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task FromAndToDatesNarrowASettingListToSettingsCreatedOrUpdatedInThatRange(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var march = await api.HasSettingAsync("backoffice-web", "march.table");
        var june = await api.HasSettingAsync("backoffice-web", "june.table");
        var moved = await api.HasSettingAsync("backoffice-web", "moved.table");
        var march10 = new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero);
        await StoredFileRows.SetActivityAsync(api, "UiSetting", march, march10, updatedOn: null);
        await StoredFileRows.SetActivityAsync(api, "UiSetting", june, new DateTimeOffset(2026, 6, 10, 9, 0, 0, TimeSpan.Zero), updatedOn: null);
        await StoredFileRows.SetActivityAsync(api, "UiSetting", moved, march10, new DateTimeOffset(2026, 6, 15, 9, 0, 0, TimeSpan.Zero));

        using var response = await api.ListSettingsAsync(Callers.ReportSvc, "backoffice-web", "&fromDate=2026-06-01&toDate=2026-06-30");

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        UiSettingsApi.SettingKeys(await response.JsonAsync()).ShouldBe(["june.table", "moved.table"], ignoreOrder: true);
    }
}
