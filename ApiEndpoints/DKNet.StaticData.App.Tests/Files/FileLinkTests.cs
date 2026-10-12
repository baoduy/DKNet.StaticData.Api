using DKNet.StaticData.App.Tests.FileGroups.Support;
using DKNet.StaticData.App.Tests.Files.Support;
using DKNet.StaticData.App.TestSupport;

namespace DKNet.StaticData.App.Tests.Files;

/// <summary>
/// Spec DRK-2235 §5 "Files and groups": an upload may name a group of the same owner, a file is linked or unlinked with
/// <c>If-Match</c> (every accepted change raises its version by 1), the file list takes a group filter, and every file
/// answer carries the group id, left out when the file has no group.
/// </summary>
public sealed class FileLinkTests(FilesHost hosts) : IClassFixture<FilesHost>
{
    /// <summary>Scenario Outline: An upload links the new file to a group.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task AnUploadLinksTheNewFileToAGroup(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var groupId = await api.HasGroupAsync("customer-1", "Onboarding pack");

        using var response = await api.UploadIntoAsync(
            Callers.OnboardingSvc, "customer-1", "passport.pdf", FilesApi.NewKey(), groupId.ToString());

        await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
        (await response.JsonAsync()).GetProperty("groupId").GetGuid().ShouldBe(groupId);
        (await api.GroupFileNamesAsync("customer-1", groupId)).ShouldBe(["passport.pdf"]);
    }

    public static IEnumerable<object[]> UnusableGroups => Databases.With(
        ["the group id \"not-a-guid\"", HttpStatusCode.BadRequest],
        ["2 group id parts", HttpStatusCode.BadRequest],
        ["a group id that names no group", HttpStatusCode.NotFound],
        ["the group id of \"Other pack\"", HttpStatusCode.NotFound]);

    /// <summary>Scenario Outline: An upload that names an unusable group stores nothing.</summary>
    [Theory]
    [MemberData(nameof(UnusableGroups))]
    public async Task AnUploadThatNamesAnUnusableGroupStoresNothing(TestDatabase database, string group, HttpStatusCode status)
    {
        var api = await hosts.OnAsync(database);
        var otherPack = await api.HasGroupAsync("customer-2", "Other pack");
        string[] parts = group switch
        {
            "the group id \"not-a-guid\"" => ["not-a-guid"],
            "2 group id parts" => [otherPack.ToString(), otherPack.ToString()],
            "a group id that names no group" => [Guid.NewGuid().ToString()],
            "the group id of \"Other pack\"" => [otherPack.ToString()],
            _ => throw new ArgumentOutOfRangeException(nameof(group), group, null)
        };

        using var response = await api.UploadIntoAsync(Callers.OnboardingSvc, "customer-1", "passport.pdf", FilesApi.NewKey(), parts);

        await response.ShouldBeProblemDetailsAsync(status);
        (await api.FileNamesAsync("customer-1")).ShouldBeEmpty();
        api.StoredBlobs().ShouldBeEmpty();
    }

    /// <summary>Scenario: The group check comes after the file checks.</summary>
    /// <remarks>
    /// The 400 must be the extension refusal of slice 2, logged with the reason <c>extension</c>, not a refusal of the
    /// group id part itself.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task TheGroupCheckComesAfterTheFileChecks(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);

        using var response = await api.UploadIntoAsync(
            Callers.OnboardingSvc, "customer-1", "page.html", FilesApi.NewKey(), Guid.NewGuid().ToString());

        await response.ShouldBeProblemDetailsAsync(HttpStatusCode.BadRequest);
        api.Host.LogCapture.Messages.ShouldContain("Upload refused: extension, by onboarding-svc");
    }

    /// <summary>Scenario: A file with no group leaves out its group field.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task AFileWithNoGroupLeavesOutItsGroupField(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);

        using var response = await api.UploadAsync(Callers.OnboardingSvc, "customer-1", "passport.pdf", Bytes.Of(1024), FilesApi.NewKey());

        await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
        var details = await response.JsonAsync();
        details.Has("groupId").ShouldBeFalse();
        (await api.FileAsync("customer-1", details.GetProperty("fileId").GetGuid())).Has("groupId").ShouldBeFalse();
    }

    /// <summary>Scenario Outline: A caller links and unlinks a file.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task ACallerLinksAndUnlinksAFile(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var groupId = await api.HasGroupAsync("customer-1", "Onboarding pack");
        var fileId = await api.HasFileAsync("customer-1", "passport.pdf");

        using var link = await api.LinkAsync(Callers.OnboardingSvc, "customer-1", fileId, FileGroupsApi.LinkBody(groupId), "\"1\"");
        await link.ShouldHaveStatusAsync(HttpStatusCode.OK);
        using var unlink = await api.LinkAsync(Callers.OnboardingSvc, "customer-1", fileId, FileGroupsApi.UnlinkBody, "\"2\"");

        link.Headers.ETag?.Tag.ShouldBe("\"2\"");
        var linked = await link.JsonAsync();
        linked.GetProperty("groupId").GetGuid().ShouldBe(groupId);
        linked.GetProperty("version").GetInt32().ShouldBe(2);
        await unlink.ShouldHaveStatusAsync(HttpStatusCode.OK);
        unlink.Headers.ETag?.Tag.ShouldBe("\"3\"");
        var details = await api.FileAsync("customer-1", fileId);
        details.GetProperty("version").GetInt32().ShouldBe(3);
        details.GetProperty("updatedBy").GetString().ShouldBe("onboarding-svc");
        details.Has("groupId").ShouldBeFalse();
    }

    /// <summary>Scenario: Linking a file to the group it already has still raises its version.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task LinkingAFileToTheGroupItAlreadyHasStillRaisesItsVersion(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var groupId = await api.HasGroupAsync("customer-1", "Onboarding pack");
        var fileId = await api.HasFileAsync("customer-1", "passport.pdf");
        await api.LinkedAsync("customer-1", fileId, groupId, "\"1\""); // linked to "Onboarding pack" at version 2

        using var response = await api.LinkAsync(Callers.OnboardingSvc, "customer-1", fileId, FileGroupsApi.LinkBody(groupId), "\"2\"");

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        response.Headers.ETag?.Tag.ShouldBe("\"3\"");
        (await response.JsonAsync()).GetProperty("version").GetInt32().ShouldBe(3);
    }

    public static IEnumerable<object[]> LinkRequests => Databases.With(
        ["no group id field and the version tag \"2\"", HttpStatusCode.BadRequest],
        ["the group id \"not-a-guid\" and the version tag \"2\"", HttpStatusCode.BadRequest],
        ["a group id that names no group and the version tag \"2\"", HttpStatusCode.NotFound],
        ["the group id of \"Other pack\" and the version tag \"2\"", HttpStatusCode.NotFound],
        ["a request to unlink and no version tag", HttpStatusCode.PreconditionRequired],
        ["a request to unlink and the version tag \"1\"", HttpStatusCode.PreconditionFailed],
        ["no group id field and no version tag", HttpStatusCode.BadRequest],
        ["a group id that names no group and the version tag \"1\"", HttpStatusCode.NotFound]);

    /// <summary>Scenario Outline: A link request is checked and a refused one changes nothing.</summary>
    [Theory]
    [MemberData(nameof(LinkRequests))]
    public async Task ALinkRequestIsCheckedAndARefusedOneChangesNothing(TestDatabase database, string request, HttpStatusCode status)
    {
        var api = await hosts.OnAsync(database);
        var groupId = await api.HasGroupAsync("customer-1", "Onboarding pack");
        var fileId = await api.HasFileAsync("customer-1", "passport.pdf");
        await api.LinkedAsync("customer-1", fileId, groupId, "\"1\""); // linked to "Onboarding pack" at version 2
        var otherPack = await api.HasGroupAsync("customer-2", "Other pack");
        var (body, ifMatch) = request switch
        {
            "no group id field and the version tag \"2\"" => ("{}", "\"2\""),
            "the group id \"not-a-guid\" and the version tag \"2\"" => ("""{"groupId":"not-a-guid"}""", "\"2\""),
            "a group id that names no group and the version tag \"2\"" => (FileGroupsApi.LinkBody(Guid.NewGuid()), "\"2\""),
            "the group id of \"Other pack\" and the version tag \"2\"" => (FileGroupsApi.LinkBody(otherPack), "\"2\""),
            "a request to unlink and no version tag" => (FileGroupsApi.UnlinkBody, null),
            "a request to unlink and the version tag \"1\"" => (FileGroupsApi.UnlinkBody, "\"1\""),
            "no group id field and no version tag" => ("{}", null),
            "a group id that names no group and the version tag \"1\"" => (FileGroupsApi.LinkBody(Guid.NewGuid()), "\"1\""),
            _ => throw new ArgumentOutOfRangeException(nameof(request), request, null)
        };

        using var response = await api.LinkAsync(Callers.OnboardingSvc, "customer-1", fileId, body, ifMatch);

        await response.ShouldBeProblemDetailsAsync(status);
        var details = await api.FileAsync("customer-1", fileId);
        details.GetProperty("groupId").GetGuid().ShouldBe(groupId);
        details.GetProperty("version").GetInt32().ShouldBe(2);
    }

    /// <summary>Scenario: Linking an unknown file answers 404.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task LinkingAnUnknownFileAnswers404(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var groupId = await api.HasGroupAsync("customer-1", "Onboarding pack");

        using var response = await api.LinkAsync(
            Callers.OnboardingSvc, "customer-1", Guid.NewGuid(), FileGroupsApi.LinkBody(groupId), "\"1\"");

        await response.ShouldBeProblemDetailsAsync(HttpStatusCode.NotFound);
        (await api.GroupAsync("customer-1", groupId)).GetProperty("version").GetInt32().ShouldBe(1);
    }

    public static IEnumerable<object[]> GroupFilters => Databases.With(
        ["the group \"Onboarding pack\"", "200 holding \"visa.pdf\" then \"passport.pdf\""],
        ["a group id that names no group", "200 with an empty list"],
        ["the group id of \"Other pack\"", "200 with an empty list"],
        ["the group id \"not-a-guid\"", "400 as problem details"]);

    /// <summary>Scenario Outline: The file list can be narrowed to one group.</summary>
    [Theory]
    [MemberData(nameof(GroupFilters))]
    public async Task TheFileListCanBeNarrowedToOneGroup(TestDatabase database, string filter, string answer)
    {
        var api = await hosts.OnAsync(database);
        var onboarding = await api.HasGroupAsync("customer-1", "Onboarding pack");
        var billing = await api.HasGroupAsync("customer-1", "Billing pack");
        await api.HasFileInGroupAsync("customer-1", "passport.pdf", onboarding);
        await api.HasFileInGroupAsync("customer-1", "visa.pdf", onboarding);
        await api.HasFileInGroupAsync("customer-1", "invoice.pdf", billing);
        var otherPack = await api.HasGroupAsync("customer-2", "Other pack");
        var groupId = filter switch
        {
            "the group \"Onboarding pack\"" => onboarding.ToString(),
            "a group id that names no group" => Guid.NewGuid().ToString(),
            "the group id of \"Other pack\"" => otherPack.ToString(),
            "the group id \"not-a-guid\"" => "not-a-guid",
            _ => throw new ArgumentOutOfRangeException(nameof(filter), filter, null)
        };

        using var response = await api.ListAsync(Callers.ReportSvc, "customer-1", $"&pageNumber=1&pageSize=100&groupId={groupId}");

        switch (answer)
        {
            case "200 holding \"visa.pdf\" then \"passport.pdf\"":
                await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
                FileGroupsApi.Names(await response.JsonAsync(), "fileName").ShouldBe(["visa.pdf", "passport.pdf"]);
                break;
            case "200 with an empty list":
                await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
                FileGroupsApi.Names(await response.JsonAsync(), "fileName").ShouldBeEmpty();
                break;
            default:
                await response.ShouldBeProblemDetailsAsync(HttpStatusCode.BadRequest);
                break;
        }
    }
}
