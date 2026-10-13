using System.Text;
using DKNet.StaticData.App.Tests.FileGroups.Support;
using DKNet.StaticData.App.Tests.Files.Support;
using DKNet.StaticData.App.Tests.SettingGroups.Support;
using DKNet.StaticData.App.Tests.UiSettings.Support;
using DKNet.StaticData.App.TestSupport;

namespace DKNet.StaticData.App.Tests.UiSettings;

/// <summary>
/// Spec DRK-2245 §5 "Create a setting": the setting fields, the value kept byte for byte and limited to 65,536 UTF-8
/// bytes of the text as sent, one setting per owner and setting key, the group of the same owner, and the fixed order of
/// a create's answers.
/// </summary>
public sealed class CreateSettingTests(FilesHost hosts) : IClassFixture<FilesHost>
{
    /// <summary>The value text spec DRK-2245 §5 names: 2 spaces after the null, <c>1.0</c> and an unescaped <c>é</c>.</summary>
    internal const string ExactValue = """{ "pinned": null,  "width": 1.0, "label": "é" }""";

    /// <summary>Scenario Outline: A caller creates a UI setting.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task ACallerCreatesAUiSetting(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);

        using var response = await api.CreateSettingAsync(Callers.LayoutSvc, "backoffice-web",
            UiSettingsApi.SettingBody("customers.list.table", "TableLayout", UiSettingsApi.TableLayout));

        await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
        var setting = await response.JsonAsync();
        var settingId = setting.GetProperty("settingId").GetGuid();
        var location = response.Headers.Location.ShouldNotBeNull();
        (location.IsAbsoluteUri ? location.PathAndQuery : location.OriginalString)
            .ShouldBe($"/v1/ui-settings/{settingId}?owner=backoffice-web");
        response.Headers.ETag?.Tag.ShouldBe("\"1\"");
        setting.GetProperty("settingKey").GetString().ShouldBe("customers.list.table");
        setting.GetProperty("settingType").GetString().ShouldBe("TableLayout");
        setting.GetProperty("owner").GetString().ShouldBe("backoffice-web");
        setting.GetProperty("schemaVersion").GetInt32().ShouldBe(1);
        setting.GetProperty("version").GetInt32().ShouldBe(1);
        setting.GetProperty("createdBy").GetString().ShouldBe("layout-svc");
        setting.ValueText().ShouldBe(UiSettingsApi.TableLayout);
        setting.Has("groupId").ShouldBeFalse();
        setting.Has("updatedBy").ShouldBeFalse();
        setting.Has("updatedOn").ShouldBeFalse();
    }

    /// <summary>Scenario Outline: The value comes back byte for byte.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task TheValueComesBackByteForByte(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var settingId = await api.HasSettingAsync("backoffice-web", "customers.list.table", value: ExactValue);

        using var response = await api.ReadSettingAsync(Callers.ReportSvc, "backoffice-web", settingId);

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        (await response.JsonAsync()).ValueText().ShouldBe("""{ "pinned": null,  "width": 1.0, "label": "é" }""");
    }

    public static IEnumerable<object[]> JsonKinds => Databases.With(
        ["""["name", "status"]"""],
        ["\"compact\""],
        ["50"],
        ["true"]);

    /// <summary>Scenario Outline: Every JSON kind except null is a valid value.</summary>
    [Theory]
    [MemberData(nameof(JsonKinds))]
    public async Task EveryJsonKindExceptNullIsAValidValue(TestDatabase database, string value)
    {
        var api = await hosts.OnAsync(database);

        using var response = await api.CreateSettingAsync(Callers.LayoutSvc, "backoffice-web",
            UiSettingsApi.SettingBody("customers.list.table", value: value));

        await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
        var settingId = (await response.JsonAsync()).GetProperty("settingId").GetGuid();
        (await api.SettingAsync("backoffice-web", settingId)).ValueText().ShouldBe(value);
    }

    public static IEnumerable<object[]> ValueSizes => Databases.With(
        ["a value text of exactly 65,536 bytes", HttpStatusCode.Created],
        ["a value text of exactly 65,536 bytes made of 2-byte letters \"é\"", HttpStatusCode.Created],
        ["a value text of 65,537 bytes", HttpStatusCode.BadRequest]);

    /// <summary>Scenario Outline: The value limit counts UTF-8 bytes of the text as sent.</summary>
    /// <remarks>Each value is a JSON string: its 2 quotes count, as every byte of the value's text does.</remarks>
    [Theory]
    [MemberData(nameof(ValueSizes))]
    public async Task TheValueLimitCountsUtf8BytesOfTheTextAsSent(TestDatabase database, string value, HttpStatusCode status)
    {
        var api = await hosts.OnAsync(database);
        var (text, bytes) = value switch
        {
            "a value text of exactly 65,536 bytes" => ("\"" + new string('a', 65_534) + "\"", 65_536),
            "a value text of exactly 65,536 bytes made of 2-byte letters \"é\"" =>
                ("\"" + string.Concat(Enumerable.Repeat("é", 32_767)) + "\"", 65_536),
            "a value text of 65,537 bytes" => ("\"" + new string('a', 65_535) + "\"", 65_537),
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
        };
        Encoding.UTF8.GetByteCount(text).ShouldBe(bytes);

        using var response = await api.CreateSettingAsync(Callers.LayoutSvc, "backoffice-web",
            UiSettingsApi.SettingBody("customers.list.table", value: text));

        if (status == HttpStatusCode.Created)
        {
            await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
            (await response.JsonAsync()).ValueText().ShouldBe(text);
        }
        else
        {
            await response.ShouldBeProblemDetailsAsync(status);
            (await api.SettingKeysAsync("backoffice-web")).ShouldBeEmpty();
        }
    }

    public static IEnumerable<object[]> BadFields => Databases.With(
        ["no setting key"],
        ["the setting key \"   \""],
        ["a setting key of 201 characters"],
        ["a setting key ending with a tab character"],
        ["no setting type"],
        ["a setting type of 51 characters"],
        ["no value"],
        ["the value null"],
        ["the schema version 0"],
        ["the schema version 1.5"],
        ["the schema version 2,147,483,648"],
        ["the group id \"not-a-guid\""]);

    /// <summary>Scenario Outline: Setting fields are checked.</summary>
    [Theory]
    [MemberData(nameof(BadFields))]
    public async Task SettingFieldsAreChecked(TestDatabase database, string fields)
    {
        var api = await hosts.OnAsync(database);
        var body = fields switch
        {
            "no setting key" => UiSettingsApi.SettingBody(null),
            "the setting key \"   \"" => UiSettingsApi.SettingBody("   "),
            "a setting key of 201 characters" => UiSettingsApi.SettingBody(new string('k', 201)),
            "a setting key ending with a tab character" => UiSettingsApi.SettingBody("customers.list.table\t"),
            "no setting type" => UiSettingsApi.SettingBody("customers.list.table", settingType: null),
            "a setting type of 51 characters" => UiSettingsApi.SettingBody("customers.list.table", new string('t', 51)),
            "no value" => UiSettingsApi.SettingBody("customers.list.table", value: null),
            "the value null" => UiSettingsApi.SettingBody("customers.list.table", value: "null"),
            "the schema version 0" => UiSettingsApi.SettingBody("customers.list.table", schemaVersion: "0"),
            "the schema version 1.5" => UiSettingsApi.SettingBody("customers.list.table", schemaVersion: "1.5"),
            "the schema version 2,147,483,648" => UiSettingsApi.SettingBody("customers.list.table", schemaVersion: "2147483648"),
            "the group id \"not-a-guid\"" => UiSettingsApi.SettingBody("customers.list.table", groupId: "\"not-a-guid\""),
            _ => throw new ArgumentOutOfRangeException(nameof(fields), fields, null)
        };

        using var response = await api.CreateSettingAsync(Callers.LayoutSvc, "backoffice-web", body);

        await response.ShouldBeProblemDetailsAsync(HttpStatusCode.BadRequest);
        (await api.SettingKeysAsync("backoffice-web")).ShouldBeEmpty();
    }

    /// <summary>Scenario: The schema version defaults to 1 and takes the largest whole number.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task TheSchemaVersionDefaultsTo1AndTakesTheLargestWholeNumber(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);

        using var customers = await api.CreateSettingAsync(Callers.LayoutSvc, "backoffice-web",
            UiSettingsApi.SettingBody("customers.list.table"));
        using var orders = await api.CreateSettingAsync(Callers.LayoutSvc, "backoffice-web",
            UiSettingsApi.SettingBody("orders.list.table", schemaVersion: "2147483647"));

        await customers.ShouldHaveStatusAsync(HttpStatusCode.Created);
        await orders.ShouldHaveStatusAsync(HttpStatusCode.Created);
        var customersId = (await customers.JsonAsync()).GetProperty("settingId").GetGuid();
        var ordersId = (await orders.JsonAsync()).GetProperty("settingId").GetGuid();
        (await api.SettingAsync("backoffice-web", customersId)).GetProperty("schemaVersion").GetInt32().ShouldBe(1);
        (await api.SettingAsync("backoffice-web", ordersId)).GetProperty("schemaVersion").GetInt32().ShouldBe(2_147_483_647);
    }

    /// <summary>Scenario Outline: A second setting with the same key is refused.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task ASecondSettingWithTheSameKeyIsRefused(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        await api.HasSettingAsync("backoffice-web", "customers.list.table");

        using var response = await api.CreateSettingAsync(Callers.LayoutSvc, "backoffice-web",
            UiSettingsApi.SettingBody("customers.list.table"));

        await response.ShouldBeProblemDetailsAsync(HttpStatusCode.Conflict);
        (await response.JsonAsync()).GetProperty("detail").GetString().ShouldNotBeNull()
            .ShouldContain("already exists", Case.Insensitive, "the 409 does not say the setting already exists");
        (await api.SettingKeysAsync("backoffice-web")).ShouldBe(["customers.list.table"]);
    }

    /// <summary>Scenario Outline: Setting keys compare exactly, case included.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task SettingKeysCompareExactlyCaseIncluded(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        await api.HasSettingAsync("backoffice-web", "customers.list.table");

        using var response = await api.CreateSettingAsync(Callers.LayoutSvc, "backoffice-web",
            UiSettingsApi.SettingBody("Customers.List.Table"));

        await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
        (await api.SettingKeysAsync("backoffice-web")).ShouldBe(["Customers.List.Table", "customers.list.table"]);
    }

    public static IEnumerable<object[]> UnusableGroups => Databases.With(
        ["a group id that names no group"],
        ["the group \"Admin screens\""]);

    /// <summary>Scenario Outline: A setting create that names an unusable group stores nothing.</summary>
    [Theory]
    [MemberData(nameof(UnusableGroups))]
    public async Task ASettingCreateThatNamesAnUnusableGroupStoresNothing(TestDatabase database, string group)
    {
        var api = await hosts.OnAsync(database);
        var adminScreens = await api.HasSettingGroupAsync("backoffice-admin", "Admin screens");
        var groupId = group == "the group \"Admin screens\"" ? adminScreens : Guid.NewGuid();

        using var response = await api.CreateSettingAsync(Callers.LayoutSvc, "backoffice-web",
            UiSettingsApi.SettingBody("customers.list.table", groupId: UiSettingsApi.Id(groupId)));

        await response.ShouldBeProblemDetailsAsync(HttpStatusCode.NotFound);
        (await api.SettingKeysAsync("backoffice-web")).ShouldBeEmpty();
    }

    /// <summary>Scenario: A setting create links the setting to a group of the same owner.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task ASettingCreateLinksTheSettingToAGroupOfTheSameOwner(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var groupId = await api.HasSettingGroupAsync("backoffice-web", "Customer screens");

        using var response = await api.CreateSettingAsync(Callers.LayoutSvc, "backoffice-web",
            UiSettingsApi.SettingBody("customers.list.table", groupId: UiSettingsApi.Id(groupId)));

        await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
        var settingId = (await response.JsonAsync()).GetProperty("settingId").GetGuid();
        (await api.SettingAsync("backoffice-web", settingId)).GetProperty("groupId").GetGuid().ShouldBe(groupId);
        (await api.SettingKeysAsync("backoffice-web", $"&groupId={groupId}")).ShouldBe(["customers.list.table"]);
    }

    public static IEnumerable<object[]> OrderedChecks => Databases.With(
        ["a setting create of \"customers.list.table\" with no owner and no value", HttpStatusCode.BadRequest],
        ["a setting create of \"customers.list.table\" with no value", HttpStatusCode.BadRequest],
        ["a setting create of \"customers.list.table\" with a group id that names no group", HttpStatusCode.NotFound],
        ["a setting create of \"customers.list.table\" with no group", HttpStatusCode.Conflict],
        ["a group create of \"Customer screens\" with no owner", HttpStatusCode.BadRequest],
        ["a group create of \"Customer screens\" with the description \"Main screens.\"", HttpStatusCode.BadRequest],
        ["a group create of \"Customer screens\" with the description \"Main screens\"", HttpStatusCode.Conflict]);

    /// <summary>Scenario Outline: A create answers its checks in a fixed order.</summary>
    [Theory]
    [MemberData(nameof(OrderedChecks))]
    public async Task ACreateAnswersItsChecksInAFixedOrder(TestDatabase database, string request, HttpStatusCode status)
    {
        var api = await hosts.OnAsync(database);
        await api.HasSettingAsync("backoffice-web", "customers.list.table");
        await api.HasSettingGroupAsync("backoffice-web", "Customer screens");

        using var response = request switch
        {
            "a setting create of \"customers.list.table\" with no owner and no value" => await api.CreateSettingAsync(
                Callers.LayoutSvc, owner: null, UiSettingsApi.SettingBody("customers.list.table", value: null)),
            "a setting create of \"customers.list.table\" with no value" => await api.CreateSettingAsync(
                Callers.LayoutSvc, "backoffice-web", UiSettingsApi.SettingBody("customers.list.table", value: null)),
            "a setting create of \"customers.list.table\" with a group id that names no group" => await api.CreateSettingAsync(
                Callers.LayoutSvc, "backoffice-web",
                UiSettingsApi.SettingBody("customers.list.table", groupId: UiSettingsApi.Id(Guid.NewGuid()))),
            "a setting create of \"customers.list.table\" with no group" => await api.CreateSettingAsync(
                Callers.LayoutSvc, "backoffice-web", UiSettingsApi.SettingBody("customers.list.table")),
            "a group create of \"Customer screens\" with no owner" => await api.CreateSettingGroupAsync(
                Callers.LayoutSvc, owner: null, SettingGroupsApi.GroupBody("Customer screens")),
            "a group create of \"Customer screens\" with the description \"Main screens.\"" => await api.CreateSettingGroupAsync(
                Callers.LayoutSvc, "backoffice-web", SettingGroupsApi.GroupBody("Customer screens", "Main screens.")),
            "a group create of \"Customer screens\" with the description \"Main screens\"" => await api.CreateSettingGroupAsync(
                Callers.LayoutSvc, "backoffice-web", SettingGroupsApi.GroupBody("Customer screens", "Main screens")),
            _ => throw new ArgumentOutOfRangeException(nameof(request), request, null)
        };

        await response.ShouldBeProblemDetailsAsync(status);
    }

    /// <summary>Scenario Outline: Two creates of one setting at the same moment make 1 setting.</summary>
    /// <remarks>"At the same moment": each create is held just before its save until both have made their checks.</remarks>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task TwoCreatesOfOneSettingAtTheSameMomentMake1Setting(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var body = UiSettingsApi.SettingBody("customers.list.table");
        SaveRaces.HoldBothSavesTogether(api);

        var answers = await Task.WhenAll(
            api.CreateSettingAsync(Callers.LayoutSvc, "backoffice-web", body),
            api.CreateSettingAsync(Callers.LayoutSvc, "backoffice-web", body));

        try
        {
            await SaveRaces.ShouldBePairAsync(answers, HttpStatusCode.Created, HttpStatusCode.Conflict);
        }
        finally
        {
            foreach (var answer in answers)
            {
                answer.Dispose();
            }
        }

        (await api.SettingKeysAsync("backoffice-web")).ShouldBe(["customers.list.table"]);
    }
}
