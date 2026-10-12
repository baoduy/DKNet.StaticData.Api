using DKNet.StaticData.App.Tests.FileGroups.Support;
using DKNet.StaticData.App.Tests.Files.Support;
using DKNet.StaticData.App.TestSupport;

namespace DKNet.StaticData.App.Tests.Integration.FileGroups;

/// <summary>
/// Brief DRK-2239 §3 rows 14 and 16: a create and an update answer the group's version tag in <c>ETag</c>, an update
/// keeps all 3 new fields, and every group route refuses a call with no owner with 400 before any other input check.
/// </summary>
public sealed class GroupAnswerTests(FilesHost hosts) : IClassFixture<FilesHost>
{
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task ACreateAndAnUpdateAnswerTheVersionTag(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);

        using var created = await api.CreateGroupAsync(
            Callers.OnboardingSvc, "customer-1", FileGroupsApi.GroupBody("Onboarding pack"), FilesApi.NewKey());
        await created.ShouldHaveStatusAsync(HttpStatusCode.Created);
        created.Headers.ETag.ShouldNotBeNull().Tag.ShouldBe("\"1\"");
        var groupId = (await created.JsonAsync()).GetProperty("groupId").GetGuid();

        using var updated = await api.UpdateGroupAsync(
            Callers.OnboardingSvc, "customer-1", groupId, FileGroupsApi.GroupBody("Pack 2"), "\"1\"");
        await updated.ShouldHaveStatusAsync(HttpStatusCode.OK);
        updated.Headers.ETag.ShouldNotBeNull().Tag.ShouldBe("\"2\"");

        using var read = await api.ReadGroupAsync(Callers.ReportSvc, "customer-1", groupId);
        await read.ShouldHaveStatusAsync(HttpStatusCode.OK);
        read.Headers.ETag.ShouldNotBeNull().Tag.ShouldBe("\"2\"");
    }

    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task AnUpdateKeepsAll3NewFields(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var groupId = await api.HasGroupAsync("customer-1", "Onboarding pack");

        using var response = await api.UpdateGroupAsync(Callers.OnboardingSvc, "customer-1", groupId,
            FileGroupsApi.GroupBody("Visa pack", "visa-application", "CUS-0043"), "\"1\"");

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        var group = await api.GroupAsync("customer-1", groupId);
        group.GetProperty("name").GetString().ShouldBe("Visa pack");
        group.GetProperty("purpose").GetString().ShouldBe("visa-application");
        group.GetProperty("externalRef").GetString().ShouldBe("CUS-0043");
    }

    public static IEnumerable<object[]> Routes => Databases.With(["create"], ["list"], ["read"], ["update"], ["delete"]);

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task AGroupCallWithNoOwnerAnswers400(TestDatabase database, string route)
    {
        var api = await hosts.OnAsync(database);
        var groupId = await api.HasGroupAsync("customer-1", "Onboarding pack");
        var rename = FileGroupsApi.GroupBody("Pack 2");

        using var response = route switch
        {
            "create" => await api.CreateGroupAsync(Callers.OnboardingSvc, owner: null, rename, FilesApi.NewKey()),
            "list" => await api.ListGroupsAsync(Callers.OnboardingSvc, owner: null),
            "read" => await api.SendAsync(HttpMethod.Get, $"{FileGroupsApi.Route}/{groupId}", Callers.OnboardingSvc),
            "update" => await api.UpdateGroupAsync(Callers.OnboardingSvc, owner: null, groupId, rename, "\"1\""),
            "delete" => await api.SendAsync(HttpMethod.Delete, $"{FileGroupsApi.Route}/{groupId}", Callers.OnboardingSvc),
            _ => throw new ArgumentOutOfRangeException(nameof(route), route, null)
        };

        await response.ShouldBeProblemDetailsAsync(HttpStatusCode.BadRequest);
        (await response.JsonAsync()).GetProperty("detail").GetString().ShouldBe(
            "The owner query parameter is required: 1 to 255 characters, not only white space, no control character.");
        var group = await api.GroupAsync("customer-1", groupId);
        group.GetProperty("name").GetString().ShouldBe("Onboarding pack");
        group.GetProperty("version").GetInt32().ShouldBe(1);
    }
}
