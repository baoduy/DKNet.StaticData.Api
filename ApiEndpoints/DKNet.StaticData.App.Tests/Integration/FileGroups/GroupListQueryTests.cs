using DKNet.StaticData.App.Tests.FileGroups.Support;
using DKNet.StaticData.App.Tests.Files.Support;
using DKNet.StaticData.App.TestSupport;

namespace DKNet.StaticData.App.Tests.Integration.FileGroups;

/// <summary>
/// Brief DRK-2239 §9 Q1: the hand-written group list keeps the paging and date rules of DKNet's list route — a missing
/// or zero page answers the first page at the default size, and a from date after the to date is refused.
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
    }
}
