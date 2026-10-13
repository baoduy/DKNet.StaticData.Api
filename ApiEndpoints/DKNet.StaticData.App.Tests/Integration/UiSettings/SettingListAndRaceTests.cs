using System.Data.SqlTypes;
using DKNet.StaticData.App.Tests.Files.Support;
using DKNet.StaticData.App.Tests.SettingGroups.Support;
using DKNet.StaticData.App.Tests.UiSettings.Support;
using DKNet.StaticData.App.TestSupport;

namespace DKNet.StaticData.App.Tests.Integration.UiSettings;

/// <summary>
/// Brief DRK-2246 rows 10 and 11, the edges the spec's scenarios leave out: the paging, date range bounds and order of
/// both lists, a rename that loses a race for a name to another rename, and no owner in any log entry when the database
/// refuses a duplicate key.
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

    /// <summary>A range that starts and ends at the same moment is a range, not a refusal.</summary>
    [Theory]
    [MemberData(nameof(Lists))]
    public async Task AListWhoseFromDateIsItsToDateIsAnswered(TestDatabase database, string list)
    {
        var api = await hosts.OnAsync(database);
        const string range = "&fromDate=2026-06-01&toDate=2026-06-01";

        using var response = list == "the settings"
            ? await api.ListSettingsAsync(Callers.ReportSvc, "backoffice-web", range)
            : await api.ListSettingGroupsAsync(Callers.ReportSvc, "backoffice-web", range);

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        (await response.JsonAsync()).GetProperty("items").GetArrayLength().ShouldBe(0);
    }

    /// <summary>Page 2 of 1 item each holds the older of 2 records.</summary>
    [Theory]
    [MemberData(nameof(Lists))]
    public async Task APageWithinTheLimitsReadsThatPage(TestDatabase database, string list)
    {
        var api = await hosts.OnAsync(database);
        await api.HasSettingAsync("backoffice-web", "a.table");
        await api.HasSettingAsync("backoffice-web", "b.table");
        await api.HasSettingGroupAsync("backoffice-web", "Group A");
        await api.HasSettingGroupAsync("backoffice-web", "Group B");

        using var response = list == "the settings"
            ? await api.ListSettingsAsync(Callers.ReportSvc, "backoffice-web", "&pageNumber=2&pageSize=1")
            : await api.ListSettingGroupsAsync(Callers.ReportSvc, "backoffice-web", "&pageNumber=2&pageSize=1");

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        var answer = await response.JsonAsync();
        answer.GetProperty("pageNumber").GetInt32().ShouldBe(2);
        answer.GetProperty("pageSize").GetInt32().ShouldBe(1);
        (list == "the settings" ? UiSettingsApi.SettingKeys(answer) : SettingGroupsApi.Names(answer))
            .ShouldBe([list == "the settings" ? "a.table" : "Group A"]);
    }

    /// <summary>
    /// A record is in the range when its creation time or its update time is, both bounds included: created or updated
    /// exactly at either bound is in; created before and updated before or after the range is out.
    /// </summary>
    [Theory]
    [MemberData(nameof(Lists))]
    public async Task ADateRangeHoldsBothBoundsAndNothingOutsideThem(TestDatabase database, string list)
    {
        var api = await hosts.OnAsync(database);
        var (from, to) = (new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 6, 30, 0, 0, 0, TimeSpan.Zero));
        var (march, april, july) = (from.AddMonths(-3), from.AddMonths(-2), to.AddDays(5));
        (string Name, DateTimeOffset CreatedOn, DateTimeOffset? UpdatedOn)[] records =
        [
            ("Created at from", from, null),
            ("Created at to", to, null),
            ("Updated at from", march, from),
            ("Updated at to", march, to),
            ("Updated before", march, april),
            ("Updated after", march, july),
            ("Created after", july, null)
        ];
        var entity = list == "the settings" ? "UiSetting" : "SettingGroup";
        foreach (var (name, createdOn, updatedOn) in records)
        {
            var id = list == "the settings"
                ? await api.HasSettingAsync("backoffice-web", name)
                : await api.HasSettingGroupAsync("backoffice-web", name);
            await StoredFileRows.SetActivityAsync(api, entity, id, createdOn, updatedOn);
        }

        const string range = "&fromDate=2026-06-01T00:00:00Z&toDate=2026-06-30T00:00:00Z";
        using var response = list == "the settings"
            ? await api.ListSettingsAsync(Callers.ReportSvc, "backoffice-web", range)
            : await api.ListSettingGroupsAsync(Callers.ReportSvc, "backoffice-web", range);

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        var answer = await response.JsonAsync();
        (list == "the settings" ? UiSettingsApi.SettingKeys(answer) : SettingGroupsApi.Names(answer))
            .ShouldBe(["Created at from", "Created at to", "Updated at from", "Updated at to"], ignoreOrder: true);
    }

    /// <summary>
    /// Records created at the same moment are listed by id, descending, as the database compares ids (SQL Server orders a
    /// uniqueidentifier by its last bytes first), so every page is the same each time.
    /// </summary>
    [Theory]
    [MemberData(nameof(Lists))]
    public async Task RecordsCreatedAtTheSameMomentAreListedByIdDescending(TestDatabase database, string list)
    {
        var api = await hosts.OnAsync(database);
        var entity = list == "the settings" ? "UiSetting" : "SettingGroup";
        var sameMoment = new DateTimeOffset(2026, 6, 10, 9, 0, 0, TimeSpan.Zero);
        var ids = new Dictionary<string, Guid>();
        foreach (var name in new[] { "First", "Second", "Third", "Fourth" })
        {
            ids[name] = list == "the settings"
                ? await api.HasSettingAsync("backoffice-web", name)
                : await api.HasSettingGroupAsync("backoffice-web", name);
            await StoredFileRows.SetActivityAsync(api, entity, ids[name], sameMoment, updatedOn: null);
        }

        var listed = list == "the settings"
            ? await api.SettingKeysAsync("backoffice-web")
            : await api.SettingGroupNamesAsync("backoffice-web");

        listed.ShouldBe(database == TestDatabase.SqlServer
            ? [.. ids.OrderByDescending(i => new SqlGuid(i.Value)).Select(i => i.Key)]
            : [.. ids.OrderByDescending(i => i.Value).Select(i => i.Key)]);
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

    public static IEnumerable<object[]> Duplicates => Databases.With(
        ["setting", "one after the other"],
        ["setting", "at the same moment"],
        ["setting group", "one after the other"],
        ["setting group", "at the same moment"]);

    /// <summary>Spec DRK-2245 "Must stay true": no log entry holds the owner, also when the database refuses a duplicate key.</summary>
    [Theory]
    [MemberData(nameof(Duplicates))]
    public async Task ADuplicateKeyTheDatabaseRefusesLogsNoOwner(TestDatabase database, string record, string timing)
    {
        var api = await hosts.OnAsync(database);
        api.Host.LogCapture.Clear();
        Func<Task<HttpResponseMessage>> create = record == "setting"
            ? () => api.CreateSettingAsync(Callers.LayoutSvc, "backoffice-web", UiSettingsApi.SettingBody("customers.list.table"))
            : () => api.CreateSettingGroupAsync(Callers.LayoutSvc, "backoffice-web", SettingGroupsApi.GroupBody("Customer screens"));

        HttpResponseMessage[] answers;
        if (timing == "at the same moment")
        {
            SaveRaces.HoldBothSavesTogether(api);
            answers = await Task.WhenAll(create(), create());
        }
        else
        {
            answers = [await create(), await create()];
        }

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
