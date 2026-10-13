using DKNet.StaticData.App.Tests.FileGroups.Support;
using DKNet.StaticData.App.Tests.Files.Support;
using DKNet.StaticData.App.Tests.SettingGroups.Support;
using DKNet.StaticData.App.Tests.UiSettings.Support;
using DKNet.StaticData.App.TestSupport;

namespace DKNet.StaticData.App.Tests.SettingGroups;

/// <summary>
/// Spec DRK-2245 §5 "Setting groups", update: an update replaces the name and the description, needs <c>If-Match</c>
/// with the current version in quotes, raises the version by exactly 1 and answers its checks in a fixed order.
/// </summary>
public sealed class UpdateSettingGroupTests(FilesHost hosts) : IClassFixture<FilesHost>
{
    /// <summary>Scenario Outline: Of 2 group updates with the same version, only the first is kept.</summary>
    /// <remarks>"At the same moment": each update is held just before its save until both have made their checks.</remarks>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task Of2GroupUpdatesWithTheSameVersionOnlyTheFirstIsKept(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var groupId = await api.HasSettingGroupAsync("backoffice-web", "Customer screens");
        SaveRaces.HoldBothSavesTogether(api);

        var answers = await Task.WhenAll(
            api.UpdateSettingGroupAsync(Callers.LayoutSvc, "backoffice-web", groupId, SettingGroupsApi.GroupBody("Client screens"), "\"1\""),
            api.UpdateSettingGroupAsync(Callers.IngestSvc, "backoffice-web", groupId, SettingGroupsApi.GroupBody("Main screens"), "\"1\""));

        try
        {
            await SaveRaces.ShouldBePairAsync(answers, HttpStatusCode.OK, HttpStatusCode.PreconditionFailed);
            var kept = await answers.Single(a => a.StatusCode == HttpStatusCode.OK).JsonAsync();
            var group = await api.SettingGroupAsync("backoffice-web", groupId);
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

    /// <summary>Scenario Outline: A group update replaces the name and the description.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task AGroupUpdateReplacesTheNameAndTheDescription(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var groupId = await api.HasSettingGroupAsync("backoffice-web", "Customer screens", "Main screens");
        (await api.SettingGroupAsync("backoffice-web", groupId)).GetProperty("description").GetString().ShouldBe("Main screens");

        using var response = await api.UpdateSettingGroupAsync(Callers.LayoutSvc, "backoffice-web", groupId,
            SettingGroupsApi.GroupBody("Client screens"), "\"1\"");

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        response.Headers.ETag.ShouldNotBeNull("the answer carries no ETag").Tag.ShouldBe("\"2\"");
        var group = await api.SettingGroupAsync("backoffice-web", groupId);
        group.GetProperty("name").GetString().ShouldBe("Client screens");
        group.Has("description").ShouldBeFalse();
        group.GetProperty("version").GetInt32().ShouldBe(2);
        group.GetProperty("updatedBy").GetString().ShouldBe("layout-svc");
        group.Has("updatedOn").ShouldBeTrue();
    }

    public static IEnumerable<object[]> OrderedChecks => Databases.With(
        ["an update with no owner, no name and no version tag", HttpStatusCode.BadRequest],
        ["an update with no name and no version tag", HttpStatusCode.BadRequest],
        ["an update of an unknown group with no version tag", HttpStatusCode.PreconditionRequired],
        ["an update of an unknown group with the version tag \"1\"", HttpStatusCode.NotFound],
        ["an update of \"Customer screens\" to \"Order screens\" with the version tag \"1\"", HttpStatusCode.PreconditionFailed],
        ["an update of \"Customer screens\" to \"Order screens\" with the version tag \"2\"", HttpStatusCode.Conflict]);

    /// <summary>Scenario Outline: A setting group update answers its checks in a fixed order.</summary>
    [Theory]
    [MemberData(nameof(OrderedChecks))]
    public async Task ASettingGroupUpdateAnswersItsChecksInAFixedOrder(TestDatabase database, string request, HttpStatusCode status)
    {
        var api = await hosts.OnAsync(database);
        var groupId = await api.HasSettingGroupAsync("backoffice-web", "Customer screens");
        await api.RaiseSettingGroupToVersion2Async("backoffice-web", groupId, "Customer screens");
        await api.HasSettingGroupAsync("backoffice-web", "Order screens");
        var toOrderScreens = SettingGroupsApi.GroupBody("Order screens");

        using var response = request switch
        {
            "an update with no owner, no name and no version tag" =>
                await api.UpdateSettingGroupAsync(Callers.LayoutSvc, owner: null, groupId, "{}", ifMatch: null),
            "an update with no name and no version tag" =>
                await api.UpdateSettingGroupAsync(Callers.LayoutSvc, "backoffice-web", groupId, "{}", ifMatch: null),
            "an update of an unknown group with no version tag" =>
                await api.UpdateSettingGroupAsync(Callers.LayoutSvc, "backoffice-web", Guid.NewGuid(), toOrderScreens, ifMatch: null),
            "an update of an unknown group with the version tag \"1\"" =>
                await api.UpdateSettingGroupAsync(Callers.LayoutSvc, "backoffice-web", Guid.NewGuid(), toOrderScreens, "\"1\""),
            "an update of \"Customer screens\" to \"Order screens\" with the version tag \"1\"" =>
                await api.UpdateSettingGroupAsync(Callers.LayoutSvc, "backoffice-web", groupId, toOrderScreens, "\"1\""),
            "an update of \"Customer screens\" to \"Order screens\" with the version tag \"2\"" =>
                await api.UpdateSettingGroupAsync(Callers.LayoutSvc, "backoffice-web", groupId, toOrderScreens, "\"2\""),
            _ => throw new ArgumentOutOfRangeException(nameof(request), request, null)
        };

        await response.ShouldBeProblemDetailsAsync(status);
    }
}
