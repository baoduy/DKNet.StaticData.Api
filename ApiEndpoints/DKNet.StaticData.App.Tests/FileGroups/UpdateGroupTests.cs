using DKNet.StaticData.App.Tests.FileGroups.Support;
using DKNet.StaticData.App.Tests.Files.Support;
using DKNet.StaticData.App.TestSupport;

namespace DKNet.StaticData.App.Tests.FileGroups;

/// <summary>
/// Spec DRK-2235 §5 "Update a group": an update replaces all 3 fields, needs <c>If-Match</c> with the current version in
/// quotes (428 without it, 412 for any other value) and answers its checks in a fixed order.
/// </summary>
public sealed class UpdateGroupTests(FilesHost hosts) : IClassFixture<FilesHost>
{
    /// <summary>Scenario Outline: A caller renames a group with its current version.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task ACallerRenamesAGroupWithItsCurrentVersion(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var groupId = await api.HasGroupAsync("customer-1", "Onboarding pack");

        using var response = await api.UpdateGroupAsync(
            Callers.OnboardingSvc, "customer-1", groupId, FileGroupsApi.GroupBody("Onboarding pack 2"), "\"1\"");

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        response.Headers.ETag?.Tag.ShouldBe("\"2\"");
        var group = await response.JsonAsync();
        group.GetProperty("name").GetString().ShouldBe("Onboarding pack 2");
        group.GetProperty("version").GetInt32().ShouldBe(2);
        group.GetProperty("updatedBy").GetString().ShouldBe("onboarding-svc");
        group.Has("updatedOn").ShouldBeTrue();
        (await api.GroupAsync("customer-1", groupId)).GetProperty("name").GetString().ShouldBe("Onboarding pack 2");
    }

    /// <summary>Scenario: An update replaces all 3 fields.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task AnUpdateReplacesAll3Fields(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var groupId = await api.HasGroupAsync("customer-1", "Onboarding pack", "customer-onboarding", "CUS-0042");

        using var response = await api.UpdateGroupAsync(
            Callers.OnboardingSvc, "customer-1", groupId, FileGroupsApi.GroupBody("Onboarding pack"), "\"1\"");

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        var group = await response.JsonAsync();
        group.Has("purpose").ShouldBeFalse();
        group.Has("externalRef").ShouldBeFalse();
        var stored = await api.GroupAsync("customer-1", groupId);
        stored.Has("purpose").ShouldBeFalse();
        stored.Has("externalRef").ShouldBeFalse();
    }

    public static IEnumerable<object[]> WrongTags => Databases.With(
        ["no version tag", HttpStatusCode.PreconditionRequired],
        ["the version tag \"1\"", HttpStatusCode.PreconditionFailed],
        ["the version tag \"*\"", HttpStatusCode.PreconditionFailed],
        ["the weak version tag \"2\"", HttpStatusCode.PreconditionFailed]);

    /// <summary>Scenario Outline: An update with a missing or wrong version tag changes nothing.</summary>
    [Theory]
    [MemberData(nameof(WrongTags))]
    public async Task AnUpdateWithAMissingOrWrongVersionTagChangesNothing(TestDatabase database, string tag, HttpStatusCode status)
    {
        var api = await hosts.OnAsync(database);
        var groupId = await api.HasGroupAsync("customer-1", "Onboarding pack");
        await api.RaiseToVersion2Async("customer-1", groupId, "Onboarding pack");
        var ifMatch = tag switch
        {
            "no version tag" => null,
            "the version tag \"1\"" => "\"1\"",
            "the version tag \"*\"" => "*",
            "the weak version tag \"2\"" => "W/\"2\"",
            _ => throw new ArgumentOutOfRangeException(nameof(tag), tag, null)
        };

        using var response = await api.UpdateGroupAsync(
            Callers.OnboardingSvc, "customer-1", groupId, FileGroupsApi.GroupBody("Pack 3"), ifMatch);

        await response.ShouldBeProblemDetailsAsync(status);
        var group = await api.GroupAsync("customer-1", groupId);
        group.GetProperty("name").GetString().ShouldBe("Onboarding pack");
        group.GetProperty("version").GetInt32().ShouldBe(2);
    }

    /// <summary>Scenario Outline: Of 2 updates with the same version, only the first is kept.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task Of2UpdatesWithTheSameVersionOnlyTheFirstIsKept(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var groupId = await api.HasGroupAsync("customer-1", "Onboarding pack");

        var answers = await Task.WhenAll(
            api.UpdateGroupAsync(Callers.OnboardingSvc, "customer-1", groupId, FileGroupsApi.GroupBody("Pack 2"), "\"1\""),
            api.UpdateGroupAsync(Callers.IngestSvc, "customer-1", groupId, FileGroupsApi.GroupBody("Pack 3"), "\"1\""));

        try
        {
            answers.Select(a => a.StatusCode).ShouldBe([HttpStatusCode.OK, HttpStatusCode.PreconditionFailed], ignoreOrder: true,
                string.Join("; ", await Task.WhenAll(answers.Select(AnswerAssertions.DescribeAsync))));
            var kept = await answers.Single(a => a.StatusCode == HttpStatusCode.OK).JsonAsync();
            var group = await api.GroupAsync("customer-1", groupId);
            group.GetProperty("version").GetInt32().ShouldBe(2);
            group.GetProperty("name").GetString().ShouldBe(kept.GetProperty("name").GetString());
        }
        finally
        {
            foreach (var answer in answers)
            {
                answer.Dispose();
            }
        }
    }

    public static IEnumerable<object[]> OrderedChecks => Databases.With(
        ["an update with no owner, no name and no version tag", HttpStatusCode.BadRequest],
        ["an update with no name and no version tag", HttpStatusCode.BadRequest],
        ["an update of an unknown group with no version tag", HttpStatusCode.PreconditionRequired],
        ["an update of an unknown group with the version tag \"1\"", HttpStatusCode.NotFound],
        ["an update of \"Onboarding pack\" with the version tag \"1\"", HttpStatusCode.PreconditionFailed]);

    /// <summary>Scenario Outline: A group update answers its checks in a fixed order.</summary>
    [Theory]
    [MemberData(nameof(OrderedChecks))]
    public async Task AGroupUpdateAnswersItsChecksInAFixedOrder(TestDatabase database, string request, HttpStatusCode status)
    {
        var api = await hosts.OnAsync(database);
        var groupId = await api.HasGroupAsync("customer-1", "Onboarding pack");
        await api.RaiseToVersion2Async("customer-1", groupId, "Onboarding pack");
        var rename = FileGroupsApi.GroupBody("Pack 2");

        using var response = request switch
        {
            "an update with no owner, no name and no version tag" =>
                await api.UpdateGroupAsync(Callers.OnboardingSvc, owner: null, groupId, "{}", ifMatch: null),
            "an update with no name and no version tag" =>
                await api.UpdateGroupAsync(Callers.OnboardingSvc, "customer-1", groupId, "{}", ifMatch: null),
            "an update of an unknown group with no version tag" =>
                await api.UpdateGroupAsync(Callers.OnboardingSvc, "customer-1", Guid.NewGuid(), rename, ifMatch: null),
            "an update of an unknown group with the version tag \"1\"" =>
                await api.UpdateGroupAsync(Callers.OnboardingSvc, "customer-1", Guid.NewGuid(), rename, "\"1\""),
            "an update of \"Onboarding pack\" with the version tag \"1\"" =>
                await api.UpdateGroupAsync(Callers.OnboardingSvc, "customer-1", groupId, rename, "\"1\""),
            _ => throw new ArgumentOutOfRangeException(nameof(request), request, null)
        };

        await response.ShouldBeProblemDetailsAsync(status);
    }
}
