using DKNet.StaticData.App.Tests.FileGroups.Support;
using DKNet.StaticData.App.Tests.Files.Support;
using DKNet.StaticData.App.Tests.UiSettings.Support;
using DKNet.StaticData.App.TestSupport;

namespace DKNet.StaticData.App.Tests.TextRules;

/// <summary>
/// Spec DRK-2245 §5 "Text rules on every route", the owner: <c>customer-1 </c> is owner <c>customer-1</c> on both
/// databases, in every answer, every <c>Location</c> header and the idempotency scope; case still counts.
/// </summary>
public sealed class OwnerTrimTests(FilesHost hosts) : IClassFixture<FilesHost>
{
    public static IEnumerable<object[]> SpacedOwners => Databases.With(
        ["reads \"customers.list.table\"", "a trailing space", "\"customers.list.table\""],
        ["reads \"customers.list.table\"", "a leading and a trailing space", "\"customers.list.table\""],
        ["lists the files", "a leading space", "\"passport.pdf\""]);

    /// <summary>Scenario Outline: An owner with outer spaces is the same owner on both databases.</summary>
    [Theory]
    [MemberData(nameof(SpacedOwners))]
    public async Task AnOwnerWithOuterSpacesIsTheSameOwnerOnBothDatabases(
        TestDatabase database, string action, string spaces, string record)
    {
        var api = await hosts.OnAsync(database);
        var settingId = await api.HasSettingAsync("backoffice-web", "customers.list.table");
        await api.HasFileAsync("backoffice-web", "passport.pdf");
        var owner = spaces switch
        {
            "a trailing space" => "backoffice-web ",
            "a leading and a trailing space" => " backoffice-web ",
            "a leading space" => " backoffice-web",
            _ => throw new ArgumentOutOfRangeException(nameof(spaces), spaces, null)
        };

        if (action == "reads \"customers.list.table\"")
        {
            using var response = await api.ReadSettingAsync(Callers.ReportSvc, owner, settingId);

            await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
            var setting = await response.JsonAsync();
            $"\"{setting.GetProperty("settingKey").GetString()}\"".ShouldBe(record);
            setting.GetProperty("owner").GetString().ShouldBe("backoffice-web");
        }
        else
        {
            using var response = await api.ListAsync(Callers.ReportSvc, owner);

            await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
            var item = (await response.JsonAsync()).GetProperty("items").EnumerateArray().ShouldHaveSingleItem();
            $"\"{item.GetProperty("fileName").GetString()}\"".ShouldBe(record);
            item.GetProperty("owner").GetString().ShouldBe("backoffice-web");
        }
    }

    /// <summary>Scenario Outline: A record saved with outer spaces in its owner lands under the trimmed owner.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task ARecordSavedWithOuterSpacesInItsOwnerLandsUnderTheTrimmedOwner(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);

        using var response = await api.CreateSettingAsync(Callers.LayoutSvc, "backoffice-web ",
            UiSettingsApi.SettingBody("customers.list.table"));

        await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
        (await response.JsonAsync()).GetProperty("owner").GetString().ShouldBe("backoffice-web");
        using var second = await api.CreateSettingAsync(Callers.LayoutSvc, "backoffice-web",
            UiSettingsApi.SettingBody("customers.list.table"));
        await second.ShouldBeProblemDetailsAsync(HttpStatusCode.Conflict);
    }

    public static IEnumerable<object[]> RepeatedCreates => Databases.With(
        ["uploaded \"passport.pdf\""],
        ["created the file group \"Onboarding pack\""]);

    /// <summary>Scenario Outline: A repeated create with outer spaces in the owner replays the first answer.</summary>
    [Theory]
    [MemberData(nameof(RepeatedCreates))]
    public async Task ARepeatedCreateWithOuterSpacesInTheOwnerReplaysTheFirstAnswer(TestDatabase database, string action)
    {
        var api = await hosts.OnAsync(database);
        Task<HttpResponseMessage> Create(string owner) => action == "uploaded \"passport.pdf\""
            ? api.UploadAsync(Callers.LayoutSvc, owner, "passport.pdf", Bytes.Of(1024), "key-1")
            : api.CreateGroupAsync(Callers.LayoutSvc, owner, FileGroupsApi.GroupBody("Onboarding pack"), "key-1");
        using var first = await Create("customer-1");
        await first.ShouldHaveStatusAsync(HttpStatusCode.Created);

        using var repeat = await Create("customer-1 ");

        await repeat.ShouldHaveStatusAsync(HttpStatusCode.Created);
        (await repeat.Content.ReadAsStringAsync()).ShouldBe(await first.Content.ReadAsStringAsync());
        if (action == "uploaded \"passport.pdf\"")
        {
            (await api.FileNamesAsync("customer-1")).ShouldBe(["passport.pdf"]);
        }
        else
        {
            (await api.GroupNamesAsync("customer-1")).ShouldBe(["Onboarding pack"]);
        }
    }

    /// <summary>Scenario: A location header carries the trimmed owner.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task ALocationHeaderCarriesTheTrimmedOwner(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);

        using var response = await api.CreateSettingAsync(Callers.LayoutSvc, "backoffice-web ",
            UiSettingsApi.SettingBody("customers.list.table"));

        await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
        var settingId = (await response.JsonAsync()).GetProperty("settingId").GetGuid();
        var location = response.Headers.Location.ShouldNotBeNull();
        (location.IsAbsoluteUri ? location.PathAndQuery : location.OriginalString)
            .ShouldBe($"/v1/ui-settings/{settingId}?owner=backoffice-web");
    }

    /// <summary>Scenario: Case still counts after trimming.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task CaseStillCountsAfterTrimming(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        await api.HasSettingAsync("backoffice-web", "customers.list.table");

        using var response = await api.ListSettingsAsync(Callers.ReportSvc, "Backoffice-Web");

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        UiSettingsApi.SettingKeys(await response.JsonAsync()).ShouldBeEmpty();
        // presence sibling: the same list under the owner as created holds the setting
        (await api.SettingKeysAsync("backoffice-web")).ShouldBe(["customers.list.table"]);
    }
}
