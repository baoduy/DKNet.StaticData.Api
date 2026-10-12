using DKNet.StaticData.App.Tests.FileGroups.Support;
using DKNet.StaticData.App.Tests.Files.Support;
using DKNet.StaticData.App.TestSupport;

namespace DKNet.StaticData.App.Tests.Integration.Files;

/// <summary>
/// Brief DRK-2237 §3 row 6: the hand-written file list keeps the paging and date rules of DKNet's list route, which it
/// replaces — a missing or zero page answers the first page at the default size, a from date after the to date is
/// refused, and a file is in the date range when its creation time or its update time is.
/// </summary>
public sealed class FileListQueryTests(FilesHost hosts) : IClassFixture<FilesHost>
{
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task AZeroPageAnswersTheFirstPageAtTheDefaultSize(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        await api.HasFileAsync("customer-1", "passport.pdf");

        using var response = await api.ListAsync(Callers.ReportSvc, "customer-1", "&pageNumber=0&pageSize=0");

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        var page = await response.JsonAsync();
        page.GetProperty("pageNumber").GetInt32().ShouldBe(1);
        page.GetProperty("pageSize").GetInt32().ShouldBe(1000); // DKNet ListQueryOptions.DefaultPageSize
        page.GetProperty("totalItemCount").GetInt32().ShouldBe(1);
        FileGroupsApi.Names(page, "fileName").ShouldBe(["passport.pdf"]);
    }

    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task AFromDateAfterTheToDateIsRefused(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);

        using var response = await api.ListAsync(Callers.ReportSvc, "customer-1", "&fromDate=2026-07-01&toDate=2026-06-01");

        await response.ShouldBeProblemDetailsAsync(HttpStatusCode.BadRequest);
        (await response.JsonAsync()).GetProperty("detail").GetString().ShouldBe("fromDate is later than toDate.");
    }

    /// <summary>A link updates a file, so an old file linked inside the range is listed by its update time.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task AnOldFileUpdatedInTheRangeIsListed(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var january = new DateTimeOffset(2026, 1, 10, 9, 0, 0, TimeSpan.Zero);
        var linkedInJune = await api.HasFileAsync("customer-1", "linked-in-june.pdf");
        await StoredFileRows.SetActivityAsync(
            api, "StoredFile", linkedInJune, january, new DateTimeOffset(2026, 6, 15, 9, 0, 0, TimeSpan.Zero));
        var linkedInMay = await api.HasFileAsync("customer-1", "linked-in-may.pdf");
        await StoredFileRows.SetActivityAsync(
            api, "StoredFile", linkedInMay, january, new DateTimeOffset(2026, 5, 15, 9, 0, 0, TimeSpan.Zero));

        using var response = await api.ListAsync(Callers.ReportSvc, "customer-1", "&fromDate=2026-06-01&toDate=2026-06-30");

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        FileGroupsApi.Names(await response.JsonAsync(), "fileName").ShouldBe(["linked-in-june.pdf"]);
    }
}
