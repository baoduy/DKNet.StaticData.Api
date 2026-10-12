using DKNet.StaticData.App.Tests.FileGroups.Support;
using DKNet.StaticData.App.Tests.Files.Support;
using DKNet.StaticData.App.TestSupport;

namespace DKNet.StaticData.App.Tests.FileGroups;

/// <summary>
/// Spec DRK-2235 §5 "Read and list groups": one group with its version tag, the owner's groups newest first, paged,
/// narrowed by optional dates and by exact purpose and external reference filters; another owner's group does not
/// exist for the caller.
/// </summary>
public sealed class ReadAndListGroupTests(FilesHost hosts) : IClassFixture<FilesHost>
{
    /// <summary>Scenario: A caller reads one group.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task ACallerReadsOneGroup(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var groupId = await api.HasGroupAsync("customer-1", "Onboarding pack");
        await api.RaiseToVersion2Async("customer-1", groupId, "Onboarding pack");

        using var response = await api.ReadGroupAsync(Callers.ReportSvc, "customer-1", groupId);

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        response.Headers.ETag?.Tag.ShouldBe("\"2\"");
        var group = await response.JsonAsync();
        group.GetProperty("groupId").GetGuid().ShouldBe(groupId);
        group.GetProperty("name").GetString().ShouldBe("Onboarding pack");
        group.GetProperty("owner").GetString().ShouldBe("customer-1");
        group.GetProperty("version").GetInt32().ShouldBe(2);
    }

    public static IEnumerable<object[]> OtherOwnerActions => Databases.With(
        ["read \"Onboarding pack\""],
        ["rename \"Onboarding pack\" to \"Pack 2\""],
        ["delete \"Onboarding pack\""]);

    /// <summary>Scenario Outline: A group of another owner does not exist for the caller.</summary>
    [Theory]
    [MemberData(nameof(OtherOwnerActions))]
    public async Task AGroupOfAnotherOwnerDoesNotExistForTheCaller(TestDatabase database, string action)
    {
        var api = await hosts.OnAsync(database);
        var groupId = await api.HasGroupAsync("customer-1", "Onboarding pack");

        using var response = action switch
        {
            "read \"Onboarding pack\"" => await api.ReadGroupAsync(Callers.OnboardingSvc, "customer-2", groupId),
            // with the group's current version tag, so the answer is the record check, not the version tag check
            "rename \"Onboarding pack\" to \"Pack 2\"" => await api.UpdateGroupAsync(
                Callers.OnboardingSvc, "customer-2", groupId, FileGroupsApi.GroupBody("Pack 2"), "\"1\""),
            "delete \"Onboarding pack\"" => await api.DeleteGroupAsync(Callers.OnboardingSvc, "customer-2", groupId),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        };

        await response.ShouldBeProblemDetailsAsync(HttpStatusCode.NotFound);
        var group = await api.GroupAsync("customer-1", groupId);
        group.GetProperty("name").GetString().ShouldBe("Onboarding pack");
        group.GetProperty("version").GetInt32().ShouldBe(1);
    }

    /// <summary>Scenario Outline: A group list shows the newest groups first, one page at a time.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task AGroupListShowsTheNewestGroupsFirstOnePageAtATime(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        await api.HasGroupAsync("customer-1", "Pack A");
        await api.HasGroupAsync("customer-1", "Pack B");
        await api.HasGroupAsync("customer-1", "Pack C");

        using var response = await api.ListGroupsAsync(Callers.ReportSvc, "customer-1", "&pageNumber=1&pageSize=2");

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        FileGroupsApi.Names(await response.JsonAsync(), "name").ShouldBe(["Pack C", "Pack B"]);
    }

    /// <summary>Scenario Outline: A group list with no dates returns old groups too.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task AGroupListWithNoDatesReturnsOldGroupsToo(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var groupId = await api.HasGroupAsync("customer-1", "Old pack");
        var fourMonthsAgo = DateTimeOffset.UtcNow.AddMonths(-4);
        await StoredFileRows.SetActivityAsync(api, "FileGroup", groupId, fourMonthsAgo, fourMonthsAgo);

        using var response = await api.ListGroupsAsync(Callers.ReportSvc, "customer-1");

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        FileGroupsApi.Names(await response.JsonAsync(), "name").ShouldBe(["Old pack"]);
    }

    public static IEnumerable<object[]> ExactFilters => Databases.With(
        ["purpose \"customer-onboarding\"", new[] { "Pack A", "Pack C" }],
        ["external reference \"CUS-0042\"", new[] { "Pack A", "Pack B" }],
        ["purpose \"customer-onboarding\" and external reference \"CUS-0042\"", new[] { "Pack A" }]);

    /// <summary>Scenario Outline: The purpose and external reference filters match exactly.</summary>
    [Theory]
    [MemberData(nameof(ExactFilters))]
    public async Task ThePurposeAndExternalReferenceFiltersMatchExactly(TestDatabase database, string filter, string[] groups)
    {
        var api = await hosts.OnAsync(database);
        await api.HasGroupAsync("customer-1", "Pack A", "customer-onboarding", "CUS-0042");
        await api.HasGroupAsync("customer-1", "Pack B", "Customer-Onboarding", "CUS-0042");
        await api.HasGroupAsync("customer-1", "Pack C", "customer-onboarding", "cus-0042");
        var query = filter switch
        {
            "purpose \"customer-onboarding\"" => "&purpose=customer-onboarding",
            "external reference \"CUS-0042\"" => "&externalRef=CUS-0042",
            "purpose \"customer-onboarding\" and external reference \"CUS-0042\"" => "&purpose=customer-onboarding&externalRef=CUS-0042",
            _ => throw new ArgumentOutOfRangeException(nameof(filter), filter, null)
        };

        using var response = await api.ListGroupsAsync(Callers.ReportSvc, "customer-1", "&pageNumber=1&pageSize=100" + query);

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        FileGroupsApi.Names(await response.JsonAsync(), "name").ShouldBe(groups, ignoreOrder: true);
    }

    /// <summary>Scenario: From and to dates narrow a group list to groups created or updated in that range.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task FromAndToDatesNarrowAGroupListToGroupsCreatedOrUpdatedInThatRange(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var march = await api.HasGroupAsync("customer-1", "March pack");
        var june = await api.HasGroupAsync("customer-1", "June pack");
        var moved = await api.HasGroupAsync("customer-1", "Moved pack");
        var march10 = new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero);
        await StoredFileRows.SetActivityAsync(api, "FileGroup", march, march10, updatedOn: null);
        await StoredFileRows.SetActivityAsync(api, "FileGroup", june, new DateTimeOffset(2026, 6, 10, 9, 0, 0, TimeSpan.Zero), updatedOn: null);
        await StoredFileRows.SetActivityAsync(api, "FileGroup", moved, march10, new DateTimeOffset(2026, 6, 15, 9, 0, 0, TimeSpan.Zero));

        using var response = await api.ListGroupsAsync(Callers.ReportSvc, "customer-1", "&fromDate=2026-06-01&toDate=2026-06-30");

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        FileGroupsApi.Names(await response.JsonAsync(), "name").ShouldBe(["June pack", "Moved pack"], ignoreOrder: true);
    }
}
