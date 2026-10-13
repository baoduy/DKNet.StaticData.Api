using DKNet.StaticData.App.Tests.FileGroups.Support;
using DKNet.StaticData.App.Tests.Files.Support;
using DKNet.StaticData.App.Tests.SettingGroups.Support;
using DKNet.StaticData.App.Tests.UiSettings.Support;
using DKNet.StaticData.App.TestSupport;

namespace DKNet.StaticData.App.Tests.TextRules;

/// <summary>
/// Spec DRK-2245 §5 "Text rules on every route", every other caller text: trimmed before it is stored or filtered on,
/// length counted on the trimmed text, a control character at the ends still refused, the display text rule on file
/// group names, and the idempotency key never trimmed.
/// </summary>
public sealed class TextTrimTests(FilesHost hosts) : IClassFixture<FilesHost>
{
    public static IEnumerable<object[]> TrimmedTexts => Databases.With(
        ["uploads a file named \"passport.pdf \" with a trailing space"],
        ["creates the file group named \" Onboarding pack \""],
        ["creates a file group with the purpose \" customer-onboarding \" and the external reference \" CUS-0042 \""],
        ["creates the setting \" customers.list.table \" of type \" TableLayout \""],
        ["creates the group \" Customer screens \" with the description \" Main screens \""]);

    /// <summary>Scenario Outline: Caller texts are trimmed before they are stored.</summary>
    [Theory]
    [MemberData(nameof(TrimmedTexts))]
    public async Task CallerTextsAreTrimmedBeforeTheyAreStored(TestDatabase database, string action)
    {
        var api = await hosts.OnAsync(database);
        const string owner = "backoffice-web";

        switch (action)
        {
            case "uploads a file named \"passport.pdf \" with a trailing space":
            {
                using var response = await api.UploadAsync(Callers.LayoutSvc, owner, "passport.pdf ", Bytes.Of(1024), FilesApi.NewKey());
                await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
                (await response.JsonAsync()).GetProperty("fileName").GetString().ShouldBe("passport.pdf");
                (await api.FileNamesAsync(owner)).ShouldBe(["passport.pdf"]);
                break;
            }
            case "creates the file group named \" Onboarding pack \"":
            {
                using var response = await api.CreateGroupAsync(
                    Callers.LayoutSvc, owner, FileGroupsApi.GroupBody(" Onboarding pack "), FilesApi.NewKey());
                await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
                (await response.JsonAsync()).GetProperty("name").GetString().ShouldBe("Onboarding pack");
                (await api.GroupNamesAsync(owner)).ShouldBe(["Onboarding pack"]);
                break;
            }
            case "creates a file group with the purpose \" customer-onboarding \" and the external reference \" CUS-0042 \"":
            {
                using var response = await api.CreateGroupAsync(Callers.LayoutSvc, owner,
                    FileGroupsApi.GroupBody("Onboarding pack", " customer-onboarding ", " CUS-0042 "), FilesApi.NewKey());
                await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
                var group = await api.GroupAsync(owner, (await response.JsonAsync()).GetProperty("groupId").GetGuid());
                group.GetProperty("purpose").GetString().ShouldBe("customer-onboarding");
                group.GetProperty("externalRef").GetString().ShouldBe("CUS-0042");
                break;
            }
            case "creates the setting \" customers.list.table \" of type \" TableLayout \"":
            {
                using var response = await api.CreateSettingAsync(Callers.LayoutSvc, owner,
                    UiSettingsApi.SettingBody(" customers.list.table ", " TableLayout "));
                await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
                var setting = await api.SettingAsync(owner, (await response.JsonAsync()).GetProperty("settingId").GetGuid());
                setting.GetProperty("settingKey").GetString().ShouldBe("customers.list.table");
                setting.GetProperty("settingType").GetString().ShouldBe("TableLayout");
                break;
            }
            case "creates the group \" Customer screens \" with the description \" Main screens \"":
            {
                using var response = await api.CreateSettingGroupAsync(Callers.LayoutSvc, owner,
                    SettingGroupsApi.GroupBody(" Customer screens ", " Main screens "));
                await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
                var group = await api.SettingGroupAsync(owner, (await response.JsonAsync()).GetProperty("groupId").GetGuid());
                group.GetProperty("name").GetString().ShouldBe("Customer screens");
                group.GetProperty("description").GetString().ShouldBe("Main screens");
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, null);
        }
    }

    public static IEnumerable<object[]> TrimmedFilters => Databases.With(
        ["the settings", "setting key \" customers.list.table \"", "\"customers.list.table\""],
        ["the file groups", "purpose \"customer-onboarding \"", "\"Onboarding pack\""],
        ["the file groups", "external reference \" CUS-0042\"", "\"Onboarding pack\""]);

    /// <summary>Scenario Outline: Text list filters are trimmed.</summary>
    [Theory]
    [MemberData(nameof(TrimmedFilters))]
    public async Task TextListFiltersAreTrimmed(TestDatabase database, string list, string filter, string record)
    {
        var api = await hosts.OnAsync(database);
        await api.HasSettingAsync("backoffice-web", "customers.list.table");
        await api.HasGroupAsync("backoffice-web", "Onboarding pack", "customer-onboarding", "CUS-0042");
        var query = "&pageNumber=1&pageSize=100" + filter switch
        {
            "setting key \" customers.list.table \"" => $"&settingKey={Uri.EscapeDataString(" customers.list.table ")}",
            "purpose \"customer-onboarding \"" => $"&purpose={Uri.EscapeDataString("customer-onboarding ")}",
            "external reference \" CUS-0042\"" => $"&externalRef={Uri.EscapeDataString(" CUS-0042")}",
            _ => throw new ArgumentOutOfRangeException(nameof(filter), filter, null)
        };

        using var response = list == "the settings"
            ? await api.ListSettingsAsync(Callers.ReportSvc, "backoffice-web", query)
            : await api.ListGroupsAsync(Callers.ReportSvc, "backoffice-web", query);

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        var page = await response.JsonAsync();
        var held = list == "the settings" ? UiSettingsApi.SettingKeys(page) : FileGroupsApi.Names(page, "name");
        held.Select(name => $"\"{name}\"").ShouldBe([record]);
    }

    /// <summary>Scenario: Length limits count the trimmed text.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task LengthLimitsCountTheTrimmedText(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var letters = new string('n', 100);

        using var response = await api.CreateSettingGroupAsync(Callers.LayoutSvc, "backoffice-web",
            SettingGroupsApi.GroupBody(letters + "  "));

        await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
        (await response.JsonAsync()).GetProperty("name").GetString().ShouldBe(letters);
    }

    public static IEnumerable<object[]> ControlCharactersAtTheEnds => Databases.With(
        ["reads the settings for an owner \"backoffice-web\" followed by a tab"],
        ["creates the setting key \"customers.list.table\" followed by a line break"],
        ["uploads a file named \"passport.pdf\" followed by a tab"],
        ["creates the file group named \"Onboarding pack\" followed by a tab"]);

    /// <summary>Scenario Outline: A control character at the ends is still refused.</summary>
    [Theory]
    [MemberData(nameof(ControlCharactersAtTheEnds))]
    public async Task AControlCharacterAtTheEndsIsStillRefused(TestDatabase database, string action)
    {
        var api = await hosts.OnAsync(database);

        using var response = action switch
        {
            "reads the settings for an owner \"backoffice-web\" followed by a tab" =>
                await api.ListSettingsAsync(Callers.LayoutSvc, "backoffice-web\t"),
            "creates the setting key \"customers.list.table\" followed by a line break" =>
                await api.CreateSettingAsync(Callers.LayoutSvc, "backoffice-web", UiSettingsApi.SettingBody("customers.list.table\n")),
            "uploads a file named \"passport.pdf\" followed by a tab" =>
                await api.UploadAsync(Callers.LayoutSvc, "backoffice-web", "passport.pdf\t", Bytes.Of(1024), FilesApi.NewKey()),
            "creates the file group named \"Onboarding pack\" followed by a tab" => await api.CreateGroupAsync(
                Callers.LayoutSvc, "backoffice-web", FileGroupsApi.GroupBody("Onboarding pack\t"), FilesApi.NewKey()),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        };

        await response.ShouldBeProblemDetailsAsync(HttpStatusCode.BadRequest);
    }

    public static IEnumerable<object[]> FileGroupNames => Databases.With(
        ["\"Onboarding pack 2026\"", HttpStatusCode.Created],
        ["\"Pack-2\"", HttpStatusCode.Created],
        ["\"Café\"", HttpStatusCode.BadRequest],
        ["\"Pack/2\"", HttpStatusCode.BadRequest]);

    /// <summary>Scenario Outline: The display text rule now holds on file group names.</summary>
    [Theory]
    [MemberData(nameof(FileGroupNames))]
    public async Task TheDisplayTextRuleNowHoldsOnFileGroupNames(TestDatabase database, string name, HttpStatusCode status)
    {
        var api = await hosts.OnAsync(database);

        using var response = await api.CreateGroupAsync(
            Callers.LayoutSvc, "backoffice-web", FileGroupsApi.GroupBody(name.Trim('"')), FilesApi.NewKey());

        if (status == HttpStatusCode.Created)
        {
            await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
            (await api.GroupNamesAsync("backoffice-web")).ShouldBe([name.Trim('"')]);
        }
        else
        {
            await response.ShouldBeProblemDetailsAsync(status);
            (await api.GroupNamesAsync("backoffice-web")).ShouldBeEmpty();
        }
    }

    /// <summary>Scenario: The idempotency key is never trimmed.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task TheIdempotencyKeyIsNeverTrimmed(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);

        using var response = await api.UploadAsync(Callers.LayoutSvc, "backoffice-web", "passport.pdf", Bytes.Of(1024), "upload-1 ");

        await response.ShouldBeProblemDetailsAsync(HttpStatusCode.BadRequest);
        (await api.FileNamesAsync("backoffice-web")).ShouldBeEmpty();
    }
}
