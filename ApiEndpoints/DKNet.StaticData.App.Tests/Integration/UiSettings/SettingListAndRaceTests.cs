using DKNet.StaticData.App.Tests.Files.Support;
using DKNet.StaticData.App.Tests.SettingGroups.Support;
using DKNet.StaticData.App.Tests.UiSettings.Support;
using DKNet.StaticData.App.TestSupport;

namespace DKNet.StaticData.App.Tests.Integration.UiSettings;

/// <summary>
/// Brief DRK-2246 rows 10 and 11, the edges the spec's scenarios leave out: the paging and date range of both lists, a
/// rename that loses a race for a name to another rename, and no owner in any log entry when a save loses a race for a
/// unique key.
/// </summary>
public sealed class SettingListAndRaceTests(FilesHost hosts) : IClassFixture<FilesHost>
{
    public static IEnumerable<object[]> Lists => Databases.With(["the settings"], ["the groups"]);

    [Theory]
    [MemberData(nameof(Lists))]
    public async Task AListWhoseFromDateIsLaterThanItsToDateIsRefused(TestDatabase database, string list)
    {
        var api = await hosts.OnAsync(database);
        const string range = "&fromDate=2026-06-30&toDate=2026-06-01";

        using var response = list == "the settings"
            ? await api.ListSettingsAsync(Callers.ReportSvc, "backoffice-web", range)
            : await api.ListSettingGroupsAsync(Callers.ReportSvc, "backoffice-web", range);

        await response.ShouldBeProblemDetailsAsync(HttpStatusCode.BadRequest);
        (await response.JsonAsync()).GetProperty("detail").GetString().ShouldBe("fromDate is later than toDate.");
    }

    public static IEnumerable<object[]> Pages => Databases.With(
        ["the settings", "&pageNumber=0&pageSize=0"],
        ["the settings", "&pageNumber=-1&pageSize=5000"],
        ["the groups", "&pageNumber=0&pageSize=0"],
        ["the groups", "&pageNumber=-1&pageSize=5000"]);

    /// <summary>A page number below 1 reads page 1; a page size below 1 or over 1,000 reads 1,000 items (spec §3).</summary>
    [Theory]
    [MemberData(nameof(Pages))]
    public async Task APageOutsideTheLimitsReadsTheFirstPageOf1000Items(TestDatabase database, string list, string page)
    {
        var api = await hosts.OnAsync(database);
        await api.HasSettingAsync("backoffice-web", "a.table");
        await api.HasSettingAsync("backoffice-web", "b.table");
        await api.HasSettingGroupAsync("backoffice-web", "Group A");
        await api.HasSettingGroupAsync("backoffice-web", "Group B");

        using var response = list == "the settings"
            ? await api.ListSettingsAsync(Callers.ReportSvc, "backoffice-web", page)
            : await api.ListSettingGroupsAsync(Callers.ReportSvc, "backoffice-web", page);

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        var answer = await response.JsonAsync();
        answer.GetProperty("pageNumber").GetInt32().ShouldBe(1);
        answer.GetProperty("pageSize").GetInt32().ShouldBe(1000);
        answer.GetProperty("items").GetArrayLength().ShouldBe(2);
    }

    /// <summary>Both renames pass the name check before either saves; the database's unique key refuses the second.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task OfTwoRenamesToOneNameAtTheSameMomentOnlyOneIsKept(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var orderScreens = await api.HasSettingGroupAsync("backoffice-web", "Order screens");
        var userScreens = await api.HasSettingGroupAsync("backoffice-web", "User screens");
        var body = SettingGroupsApi.GroupBody("Main screens");
        SaveRaces.HoldBothSavesTogether(api);

        var answers = await Task.WhenAll(
            api.UpdateSettingGroupAsync(Callers.LayoutSvc, "backoffice-web", orderScreens, body, "\"1\""),
            api.UpdateSettingGroupAsync(Callers.LayoutSvc, "backoffice-web", userScreens, body, "\"1\""));

        try
        {
            await SaveRaces.ShouldBePairAsync(answers, HttpStatusCode.OK, HttpStatusCode.Conflict);
            var refused = await answers.Single(a => a.StatusCode == HttpStatusCode.Conflict).JsonAsync();
            refused.GetProperty("detail").GetString().ShouldBe("The setting group name is already used by this owner.");
        }
        finally
        {
            foreach (var answer in answers)
            {
                answer.Dispose();
            }
        }

        // the kept rename took the name; the other group keeps its own
        var names = await api.SettingGroupNamesAsync("backoffice-web");
        names.Count.ShouldBe(2);
        names.Count(n => n == "Main screens").ShouldBe(1);
    }

    public static IEnumerable<object[]> LostRaces => Databases.With(["2 setting creates"], ["2 setting group creates"]);

    /// <summary>Spec DRK-2245 "Must stay true": no log entry holds the owner, also when the database refuses a duplicate key.</summary>
    [Theory]
    [MemberData(nameof(LostRaces))]
    public async Task ASaveThatLosesARaceForAUniqueKeyLogsNoOwner(TestDatabase database, string race)
    {
        var api = await hosts.OnAsync(database);
        api.Host.LogCapture.Clear();
        SaveRaces.HoldBothSavesTogether(api);
        Func<Task<HttpResponseMessage>> create = race == "2 setting creates"
            ? () => api.CreateSettingAsync(Callers.LayoutSvc, "backoffice-web", UiSettingsApi.SettingBody("customers.list.table"))
            : () => api.CreateSettingGroupAsync(Callers.LayoutSvc, "backoffice-web", SettingGroupsApi.GroupBody("Customer screens"));

        var answers = await Task.WhenAll(create(), create());

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

        api.Host.LogCapture.Entries.ShouldNotContain(e => e.Holds("backoffice-web"), "a log entry holds the owner");
    }
}
