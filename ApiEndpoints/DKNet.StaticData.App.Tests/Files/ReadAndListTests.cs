using DKNet.StaticData.App.Tests.Files.Support;
using DKNet.StaticData.App.TestSupport;

namespace DKNet.StaticData.App.Tests.Files;

/// <summary>
/// Spec DRK-2206 §5 "Read and list": one file's details with its version tag, and the owner's files newest first,
/// paged, narrowed by optional from and to dates, with no activity window.
/// </summary>
public sealed class ReadAndListTests(FilesHost hosts) : IClassFixture<FilesHost>
{
    /// <summary>Scenario: A caller reads one file's details.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task ACallerReadsOneFilesDetails(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var fileId = await api.HasFileAsync("customer-1", "passport.pdf");

        using var response = await api.ReadAsync(Callers.ReportSvc, "customer-1", fileId);

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        response.Headers.ETag?.Tag.ShouldBe("\"1\"");
        var details = await response.JsonAsync();
        details.GetProperty("fileId").GetGuid().ShouldBe(fileId);
        details.GetProperty("owner").GetString().ShouldBe("customer-1");
        details.GetProperty("fileName").GetString().ShouldBe("passport.pdf");
        details.GetProperty("contentType").GetString().ShouldBe("application/pdf");
        details.GetProperty("sizeBytes").GetInt64().ShouldBe(1024);
        details.GetProperty("version").GetInt32().ShouldBe(1);
        details.GetProperty("createdBy").GetString().ShouldBe("onboarding-svc");
    }

    /// <summary>Scenario Outline: A list shows the newest files first, one page at a time.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task AListShowsTheNewestFilesFirstOnePageAtATime(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        await api.HasFileAsync("customer-1", "a.pdf");
        await api.HasFileAsync("customer-1", "b.pdf");
        await api.HasFileAsync("customer-1", "c.pdf");

        using var response = await api.ListAsync(Callers.ReportSvc, "customer-1", "&pageNumber=1&pageSize=2");

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        Names(await response.JsonAsync()).ShouldBe(["c.pdf", "b.pdf"]);
    }

    /// <summary>Scenario Outline: A list with no dates returns old files too.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task AListWithNoDatesReturnsOldFilesToo(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var fileId = await api.HasFileAsync("customer-1", "contract.pdf");
        await StoredFileRows.SetCreatedOnAsync(api, fileId, DateTimeOffset.UtcNow.AddMonths(-4));

        using var response = await api.ListAsync(Callers.ReportSvc, "customer-1");

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        Names(await response.JsonAsync()).ShouldBe(["contract.pdf"]);
    }

    /// <summary>Scenario: From and to dates narrow a list.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task FromAndToDatesNarrowAList(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var march = await api.HasFileAsync("customer-1", "march.pdf");
        var june = await api.HasFileAsync("customer-1", "june.pdf");
        await StoredFileRows.SetCreatedOnAsync(api, march, new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero));
        await StoredFileRows.SetCreatedOnAsync(api, june, new DateTimeOffset(2026, 6, 10, 9, 0, 0, TimeSpan.Zero));

        using var response = await api.ListAsync(Callers.ReportSvc, "customer-1", "&fromDate=2026-06-01&toDate=2026-06-30");

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        Names(await response.JsonAsync()).ShouldBe(["june.pdf"]);
    }

    private static string[] Names(JsonElement page) =>
        [.. page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("fileName").GetString()!)];
}
