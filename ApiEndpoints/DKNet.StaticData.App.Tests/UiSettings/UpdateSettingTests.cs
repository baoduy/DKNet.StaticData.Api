using DKNet.StaticData.App.Tests.FileGroups.Support;
using DKNet.StaticData.App.Tests.Files.Support;
using DKNet.StaticData.App.Tests.SettingGroups.Support;
using DKNet.StaticData.App.Tests.UiSettings.Support;
using DKNet.StaticData.App.TestSupport;

namespace DKNet.StaticData.App.Tests.UiSettings;

/// <summary>
/// Spec DRK-2245 §5 "Update a setting": an update replaces the type, the group, the value and the schema version, never
/// the setting key; it needs <c>If-Match</c> with the current version in quotes (428 without it, 412 for any other value),
/// raises the version by exactly 1 and answers its checks in a fixed order.
/// </summary>
public sealed class UpdateSettingTests(FilesHost hosts) : IClassFixture<FilesHost>
{
    private const string NewValue = """{"columns":["name","status","city"],"sort":"city","pageSize":25}""";

    /// <summary>Scenario Outline: A caller replaces a setting with its current version.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task ACallerReplacesASettingWithItsCurrentVersion(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var settingId = await api.HasSettingAsync("backoffice-web", "customers.list.table");

        using var response = await api.UpdateSettingAsync(Callers.LayoutSvc, "backoffice-web", settingId,
            UiSettingsApi.SettingBody(null, "TableLayout", NewValue, schemaVersion: "2"), "\"1\"");

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        response.Headers.ETag?.Tag.ShouldBe("\"2\"");
        var answered = await response.JsonAsync();
        answered.GetProperty("version").GetInt32().ShouldBe(2);
        var setting = await api.SettingAsync("backoffice-web", settingId);
        setting.ValueText().ShouldBe(NewValue);
        setting.GetProperty("schemaVersion").GetInt32().ShouldBe(2);
        setting.GetProperty("version").GetInt32().ShouldBe(2);
        setting.GetProperty("updatedBy").GetString().ShouldBe("layout-svc");
        setting.Has("updatedOn").ShouldBeTrue();
        setting.GetProperty("createdBy").GetString().ShouldBe("layout-svc");
    }

    /// <summary>Scenario: An update replaces every field.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task AnUpdateReplacesEveryField(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var groupId = await api.HasSettingGroupAsync("backoffice-web", "Customer screens");
        var settingId = await api.HasSettingAsync("backoffice-web", "customers.list.table", groupId, schemaVersion: "3");
        var before = await api.SettingAsync("backoffice-web", settingId);
        before.GetProperty("groupId").GetGuid().ShouldBe(groupId);
        before.GetProperty("schemaVersion").GetInt32().ShouldBe(3);

        // only a type and a value
        using var response = await api.UpdateSettingAsync(Callers.LayoutSvc, "backoffice-web", settingId,
            UiSettingsApi.SettingBody(null, "TableLayout", NewValue), "\"1\"");

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        var setting = await api.SettingAsync("backoffice-web", settingId);
        setting.Has("groupId").ShouldBeFalse();
        setting.GetProperty("schemaVersion").GetInt32().ShouldBe(1);
    }

    /// <summary>Scenario: An update ignores a setting key in the request.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task AnUpdateIgnoresASettingKeyInTheRequest(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var settingId = await api.HasSettingAsync("backoffice-web", "customers.list.table");

        using var response = await api.UpdateSettingAsync(Callers.LayoutSvc, "backoffice-web", settingId,
            UiSettingsApi.SettingBody("orders.list.table", "TableLayout", NewValue), "\"1\"");

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        (await api.SettingAsync("backoffice-web", settingId)).GetProperty("settingKey").GetString().ShouldBe("customers.list.table");
        (await api.SettingKeysAsync("backoffice-web")).ShouldBe(["customers.list.table"]);
    }

    /// <summary>Scenario: An update with no change still raises the version.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task AnUpdateWithNoChangeStillRaisesTheVersion(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var groupId = await api.HasSettingGroupAsync("backoffice-web", "Customer screens");
        var settingId = await api.HasSettingAsync("backoffice-web", "customers.list.table", groupId);
        await api.RaiseSettingToVersion2Async("backoffice-web", settingId, groupId);

        // its current type, group, value and schema version
        using var response = await api.UpdateSettingAsync(Callers.LayoutSvc, "backoffice-web", settingId,
            UiSettingsApi.SettingBody(null, "TableLayout", UiSettingsApi.TableLayout, UiSettingsApi.Id(groupId), "1"), "\"2\"");

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        response.Headers.ETag?.Tag.ShouldBe("\"3\"");
        (await api.SettingAsync("backoffice-web", settingId)).GetProperty("version").GetInt32().ShouldBe(3);
    }

    public static IEnumerable<object[]> WrongTags => Databases.With(
        ["no version tag", HttpStatusCode.PreconditionRequired],
        ["the version tag \"1\"", HttpStatusCode.PreconditionFailed],
        ["the version tag \"*\"", HttpStatusCode.PreconditionFailed],
        ["the weak version tag \"2\"", HttpStatusCode.PreconditionFailed]);

    /// <summary>Scenario Outline: An update with a missing or wrong version tag changes nothing.</summary>
    [Theory]
    [MemberData(nameof(WrongTags))]
    public async Task AnUpdateWithAMissingOrWrongVersionTagChangesNothing(TestDatabase database, string tag, HttpStatusCode status)
    {
        var api = await hosts.OnAsync(database);
        var settingId = await api.HasSettingAsync("backoffice-web", "customers.list.table");
        await api.RaiseSettingToVersion2Async("backoffice-web", settingId);
        var ifMatch = tag switch
        {
            "no version tag" => null,
            "the version tag \"1\"" => "\"1\"",
            "the version tag \"*\"" => "*",
            "the weak version tag \"2\"" => "W/\"2\"",
            _ => throw new ArgumentOutOfRangeException(nameof(tag), tag, null)
        };

        using var response = await api.UpdateSettingAsync(Callers.LayoutSvc, "backoffice-web", settingId,
            UiSettingsApi.SettingBody(null, value: NewValue), ifMatch);

        await response.ShouldBeProblemDetailsAsync(status);
        var setting = await api.SettingAsync("backoffice-web", settingId);
        setting.ValueText().ShouldBe(UiSettingsApi.TableLayout);
        setting.GetProperty("version").GetInt32().ShouldBe(2);
    }

    /// <summary>Scenario Outline: Of 2 updates with the same version, only the first is kept.</summary>
    /// <remarks>"At the same moment": each update is held just before its save until both have made their checks.</remarks>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task Of2UpdatesWithTheSameVersionOnlyTheFirstIsKept(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var settingId = await api.HasSettingAsync("backoffice-web", "customers.list.table");
        SaveRaces.HoldBothSavesTogether(api);

        var answers = await Task.WhenAll(
            api.UpdateSettingAsync(Callers.LayoutSvc, "backoffice-web", settingId,
                UiSettingsApi.SettingBody(null, value: """{"by":"layout-svc"}"""), "\"1\""),
            api.UpdateSettingAsync(Callers.IngestSvc, "backoffice-web", settingId,
                UiSettingsApi.SettingBody(null, value: """{"by":"ingest-svc"}"""), "\"1\""));

        try
        {
            await SaveRaces.ShouldBePairAsync(answers, HttpStatusCode.OK, HttpStatusCode.PreconditionFailed);
            var kept = await answers.Single(a => a.StatusCode == HttpStatusCode.OK).JsonAsync();
            var setting = await api.SettingAsync("backoffice-web", settingId);
            setting.GetProperty("version").GetInt32().ShouldBe(2);
            setting.ValueText().ShouldBe(kept.ValueText());
        }
        finally
        {
            foreach (var answer in answers)
            {
                answer.Dispose();
            }
        }
    }

    public static IEnumerable<object[]> OrderedChecks => Databases.With(
        ["an update with no owner, no value and no version tag", HttpStatusCode.BadRequest],
        ["an update with no value and no version tag", HttpStatusCode.BadRequest],
        ["an update of an unknown setting with no version tag", HttpStatusCode.PreconditionRequired],
        ["an update of an unknown setting with a group id that names no group and the version tag \"1\"", HttpStatusCode.NotFound],
        ["an update of \"customers.list.table\" with a group id that names no group and the version tag \"1\"", HttpStatusCode.NotFound],
        ["an update of \"customers.list.table\" with the version tag \"1\"", HttpStatusCode.PreconditionFailed]);

    /// <summary>Scenario Outline: A setting update answers its checks in a fixed order.</summary>
    [Theory]
    [MemberData(nameof(OrderedChecks))]
    public async Task ASettingUpdateAnswersItsChecksInAFixedOrder(TestDatabase database, string request, HttpStatusCode status)
    {
        var api = await hosts.OnAsync(database);
        var settingId = await api.HasSettingAsync("backoffice-web", "customers.list.table");
        await api.RaiseSettingToVersion2Async("backoffice-web", settingId);
        var noValue = UiSettingsApi.SettingBody(null, value: null);
        var intoNoGroup = UiSettingsApi.SettingBody(null, value: NewValue, groupId: UiSettingsApi.Id(Guid.NewGuid()));

        using var response = request switch
        {
            "an update with no owner, no value and no version tag" =>
                await api.UpdateSettingAsync(Callers.LayoutSvc, owner: null, settingId, noValue, ifMatch: null),
            "an update with no value and no version tag" =>
                await api.UpdateSettingAsync(Callers.LayoutSvc, "backoffice-web", settingId, noValue, ifMatch: null),
            "an update of an unknown setting with no version tag" => await api.UpdateSettingAsync(
                Callers.LayoutSvc, "backoffice-web", Guid.NewGuid(), UiSettingsApi.SettingBody(null, value: NewValue), ifMatch: null),
            "an update of an unknown setting with a group id that names no group and the version tag \"1\"" =>
                await api.UpdateSettingAsync(Callers.LayoutSvc, "backoffice-web", Guid.NewGuid(), intoNoGroup, "\"1\""),
            "an update of \"customers.list.table\" with a group id that names no group and the version tag \"1\"" =>
                await api.UpdateSettingAsync(Callers.LayoutSvc, "backoffice-web", settingId, intoNoGroup, "\"1\""),
            "an update of \"customers.list.table\" with the version tag \"1\"" => await api.UpdateSettingAsync(
                Callers.LayoutSvc, "backoffice-web", settingId, UiSettingsApi.SettingBody(null, value: NewValue), "\"1\""),
            _ => throw new ArgumentOutOfRangeException(nameof(request), request, null)
        };

        await response.ShouldBeProblemDetailsAsync(status);
    }

    /// <summary>Scenario: A group deleted during a setting update makes the update fail.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task AGroupDeletedDuringASettingUpdateMakesTheUpdateFail(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var groupId = await api.HasSettingGroupAsync("backoffice-web", "Customer screens");
        var settingId = await api.HasSettingAsync("backoffice-web", "customers.list.table");
        // the group is deleted after the update checks it and before the update is saved
        var groupDeleted = SaveRaces.DuringTheNextSave(api,
            () => api.DeleteSettingGroupAsync(Callers.LayoutSvc, "backoffice-web", groupId));

        using var response = await api.UpdateSettingAsync(Callers.LayoutSvc, "backoffice-web", settingId,
            UiSettingsApi.SettingBody(null, value: NewValue, groupId: UiSettingsApi.Id(groupId)), "\"1\"");

        await response.ShouldBeProblemDetailsAsync(HttpStatusCode.NotFound);
        using (var deleted = await groupDeleted)
        {
            await deleted.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        }

        var setting = await api.SettingAsync("backoffice-web", settingId);
        setting.Has("groupId").ShouldBeFalse();
        setting.GetProperty("version").GetInt32().ShouldBe(1);
        setting.ValueText().ShouldBe(UiSettingsApi.TableLayout);
    }
}
