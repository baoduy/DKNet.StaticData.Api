using System.Text;
using DKNet.StaticData.App.Tests.FileGroups.Support;
using DKNet.StaticData.App.Tests.Files.Support;
using DKNet.StaticData.App.TestSupport;

namespace DKNet.StaticData.App.Tests.Integration.Files;

/// <summary>
/// Brief DRK-2237 §3 rows 4-5 and §6a D8: the link route reads its body whatever the content type, answers the file's
/// version tag in <c>ETag</c>, and never overwrites a change another call saved between its version check and its save.
/// </summary>
public sealed class LinkAnswerTests(FilesHost hosts) : IClassFixture<FilesHost>
{
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task ALinkSentAsPlainTextIsReadAndAnswersTheVersionTag(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var groupId = await api.HasGroupAsync("customer-1", "Onboarding pack");
        var fileId = await api.HasFileAsync("customer-1", "passport.pdf");

        using var response = await LinkAsync(api, fileId, FileGroupsApi.LinkBody(groupId), "text/plain");

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        response.Headers.ETag.ShouldNotBeNull().Tag.ShouldBe("\"2\"");
        (await api.FileAsync("customer-1", fileId)).GetProperty("groupId").GetGuid().ShouldBe(groupId);
    }

    public static IEnumerable<object[]> RefusedBodies => Databases.With(
        ["42", "application/json"],
        ["", "application/json"],
        ["not json", "text/plain"],
        ["{\"groupId\":42}", "application/xml"]);

    [Theory]
    [MemberData(nameof(RefusedBodies))]
    public async Task ABodyThatIsNotALinkAnswers400NotA415(TestDatabase database, string body, string contentType)
    {
        var api = await hosts.OnAsync(database);
        var fileId = await api.HasFileAsync("customer-1", "passport.pdf");

        using var response = await LinkAsync(api, fileId, body, contentType);

        await response.ShouldBeProblemDetailsAsync(HttpStatusCode.BadRequest);
        (await response.JsonAsync()).GetProperty("detail").GetString()
            .ShouldBe("The body must be a JSON object whose \"groupId\" is a group id, or null to unlink.");
        (await api.FileAsync("customer-1", fileId)).GetProperty("version").GetInt32().ShouldBe(1);
    }

    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task AFileChangedAfterTheVersionCheckMakesTheLinkAnswer412(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var groupId = await api.HasGroupAsync("customer-1", "Onboarding pack");
        var fileId = await api.HasFileAsync("customer-1", "passport.pdf");
        var s = api.Server;
        api.Host.BeforeSave.Run(async () => (await s.ExecuteAsync(
            api.Host.ConnectionString,
            $"UPDATE {s.Quote("files")}.{s.Quote("StoredFiles")} SET {s.Quote("Version")} = 2 WHERE {s.Quote("Id")} = @id",
            ("id", fileId))).ShouldBe(1));

        using var response = await api.LinkAsync(Callers.OnboardingSvc, "customer-1", fileId, FileGroupsApi.LinkBody(groupId), "\"1\"");

        await response.ShouldBeProblemDetailsAsync(HttpStatusCode.PreconditionFailed);
        var details = await api.FileAsync("customer-1", fileId);
        details.Has("groupId").ShouldBeFalse();
        details.GetProperty("version").GetInt32().ShouldBe(2);
    }

    private static Task<HttpResponseMessage> LinkAsync(FilesApi api, Guid fileId, string body, string contentType) =>
        api.SendAsync(
            HttpMethod.Put, $"{FilesApi.Route}/{fileId}/group{FilesApi.OwnerQuery("customer-1")}", Callers.OnboardingSvc,
            new StringContent(body, Encoding.UTF8, contentType), ifMatch: "\"1\"");
}
