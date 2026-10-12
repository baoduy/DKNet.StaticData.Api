using DKNet.StaticData.App.Tests.FileGroups.Support;
using DKNet.StaticData.App.Tests.Files.Support;
using DKNet.StaticData.App.TestSupport;

namespace DKNet.StaticData.App.Tests.Integration.FileGroups;

/// <summary>
/// Brief DRK-2239 §9 Q1: the hand-written group list keeps the paging and date rules of DKNet's list route — a missing
/// or zero page answers the first page at the default size, a page is the one asked for, the date range is inclusive at
/// both ends, and a from date after the to date is refused.
/// </summary>
public sealed class GroupListQueryTests(FilesHost hosts) : IClassFixture<FilesHost>
{
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task AZeroPageAnswersTheFirstPageAtTheDefaultSize(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        await api.HasGroupAsync("customer-1", "Pack A");

        using var response = await api.ListGroupsAsync(Callers.ReportSvc, "customer-1", "&pageNumber=0&pageSize=0");

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        var page = await response.JsonAsync();
        page.GetProperty("pageNumber").GetInt32().ShouldBe(1);
        page.GetProperty("pageSize").GetInt32().ShouldBe(1000); // DKNet ListQueryOptions.DefaultPageSize
        page.GetProperty("totalItemCount").GetInt32().ShouldBe(1);
        FileGroupsApi.Names(page, "name").ShouldBe(["Pack A"]);
    }

    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task AFromDateAfterTheToDateIsRefused(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);

        using var response = await api.ListGroupsAsync(Callers.ReportSvc, "customer-1", "&fromDate=2026-07-01&toDate=2026-06-01");

        await response.ShouldBeProblemDetailsAsync(HttpStatusCode.BadRequest);
        (await response.JsonAsync()).GetProperty("detail").GetString().ShouldBe("fromDate is later than toDate.");
    }

    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task ThePageNumberAndSizeAreKept(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        await api.HasGroupAsync("customer-1", "Pack A");
        await api.HasGroupAsync("customer-1", "Pack B");

        using var response = await api.ListGroupsAsync(Callers.ReportSvc, "customer-1", "&pageNumber=2&pageSize=1");

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        var page = await response.JsonAsync();
        page.GetProperty("pageNumber").GetInt32().ShouldBe(2);
        page.GetProperty("pageSize").GetInt32().ShouldBe(1);
        FileGroupsApi.Names(page, "name").ShouldBe(["Pack A"]);
    }

    /// <summary>Both bounds are inclusive, and an update outside them does not bring an older group in.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task AnUpdateTimeIsInTheRangeOnlyBetweenItsInclusiveBounds(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var january = new DateTimeOffset(2026, 1, 10, 9, 0, 0, TimeSpan.Zero);
        var updates = new (string Name, DateTimeOffset UpdatedOn)[]
        {
            ("Updated on the from date", new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)),
            ("Updated on the to date", new DateTimeOffset(2026, 6, 30, 0, 0, 0, TimeSpan.Zero)),
            ("Updated before", new DateTimeOffset(2026, 5, 31, 23, 59, 59, TimeSpan.Zero)),
            ("Updated after", new DateTimeOffset(2026, 6, 30, 0, 0, 1, TimeSpan.Zero))
        };
        foreach (var (name, updatedOn) in updates)
        {
            var groupId = await api.HasGroupAsync("customer-1", name);
            await StoredFileRows.SetActivityAsync(api, "FileGroup", groupId, january, updatedOn);
        }

        var createdOnTheToDate = await api.HasGroupAsync("customer-1", "Created on the to date");
        await StoredFileRows.SetActivityAsync(
            api, "FileGroup", createdOnTheToDate, new DateTimeOffset(2026, 6, 30, 0, 0, 0, TimeSpan.Zero), updatedOn: null);

        using var response = await api.ListGroupsAsync(
            Callers.ReportSvc, "customer-1", "&fromDate=2026-06-01T00:00:00Z&toDate=2026-06-30T00:00:00Z");

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        FileGroupsApi.Names(await response.JsonAsync(), "name")
            .ShouldBe(["Updated on the from date", "Updated on the to date", "Created on the to date"], ignoreOrder: true);
    }
}
