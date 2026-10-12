using DKNet.StaticData.App.Tests.FileGroups.Support;
using DKNet.StaticData.App.Tests.Files.Support;
using DKNet.StaticData.App.TestSupport;

namespace DKNet.StaticData.App.Tests.FileGroups;

/// <summary>
/// Spec DRK-2235 §5 "Callers and roles": every new <c>GET</c> needs <c>staticdata.read</c>, every new <c>POST</c>,
/// <c>PUT</c> and <c>DELETE</c> needs <c>staticdata.write</c>; the role is checked before every input check and a 403
/// reserves no idempotency key.
/// </summary>
public sealed class GroupRolesTests(FilesHost hosts) : IClassFixture<FilesHost>
{
    public static IEnumerable<object[]> RefusedCallers => Databases.With(
        ["report-svc", "create the group \"Visa pack\""],
        ["report-svc", "rename \"Onboarding pack\" to \"Pack 2\""],
        ["report-svc", "delete \"Onboarding pack\""],
        ["report-svc", "unlink \"passport.pdf\" from its group"],
        ["ingest-svc", "list the groups"],
        ["ingest-svc", "read \"Onboarding pack\""],
        ["ingest-svc", "list the files of \"Onboarding pack\""]);

    /// <summary>Scenario Outline: A group route refuses a caller without the role it needs.</summary>
    [Theory]
    [MemberData(nameof(RefusedCallers))]
    public async Task AGroupRouteRefusesACallerWithoutTheRoleItNeeds(TestDatabase database, string caller, string action)
    {
        var api = await hosts.OnAsync(database);
        var groupId = await api.HasGroupAsync("customer-1", "Onboarding pack");
        var fileId = await api.HasFileInGroupAsync("customer-1", "passport.pdf", groupId);
        var token = caller == "report-svc" ? Callers.ReportSvc : Callers.IngestSvc;

        using var response = action switch
        {
            "create the group \"Visa pack\"" => await api.CreateGroupAsync(
                token, "customer-1", FileGroupsApi.GroupBody("Visa pack"), FilesApi.NewKey()),
            "rename \"Onboarding pack\" to \"Pack 2\"" => await api.UpdateGroupAsync(
                token, "customer-1", groupId, FileGroupsApi.GroupBody("Pack 2"), "\"1\""),
            "delete \"Onboarding pack\"" => await api.DeleteGroupAsync(token, "customer-1", groupId),
            "unlink \"passport.pdf\" from its group" => await api.LinkAsync(
                token, "customer-1", fileId, FileGroupsApi.UnlinkBody, "\"1\""),
            "list the groups" => await api.ListGroupsAsync(token, "customer-1"),
            "read \"Onboarding pack\"" => await api.ReadGroupAsync(token, "customer-1", groupId),
            "list the files of \"Onboarding pack\"" => await api.ListAsync(token, "customer-1", $"&groupId={groupId}"),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        };

        await response.ShouldBeProblemDetailsAsync(HttpStatusCode.Forbidden);
        // the group and the file link are unchanged
        (await api.GroupNamesAsync("customer-1")).ShouldBe(["Onboarding pack"]);
        var group = await api.GroupAsync("customer-1", groupId);
        group.GetProperty("version").GetInt32().ShouldBe(1);
        var file = await api.FileAsync("customer-1", fileId);
        file.GetProperty("groupId").GetGuid().ShouldBe(groupId);
        file.GetProperty("version").GetInt32().ShouldBe(1);
    }

    /// <summary>Scenario: The role check comes before every input check on a group create.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task TheRoleCheckComesBeforeEveryInputCheckOnAGroupCreate(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);

        // no owner, no idempotency key and no name
        using var response = await api.CreateGroupAsync(Callers.ReportSvc, owner: null, "{}", key: null);

        await response.ShouldBeProblemDetailsAsync(HttpStatusCode.Forbidden);
    }

    /// <summary>Scenario: A group create refused for its role does not use up its idempotency key.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task AGroupCreateRefusedForItsRoleDoesNotUseUpItsIdempotencyKey(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        using (var refused = await api.CreateGroupAsync(
                   Callers.Holding("ingest-svc"), "customer-1", FileGroupsApi.GroupBody("Onboarding pack"), "group-7"))
        {
            await refused.ShouldBeProblemDetailsAsync(HttpStatusCode.Forbidden);
        }

        using var response = await api.CreateGroupAsync(
            Callers.Holding("ingest-svc", Callers.Write), "customer-1", FileGroupsApi.GroupBody("Onboarding pack"), "group-7");

        await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
    }
}
