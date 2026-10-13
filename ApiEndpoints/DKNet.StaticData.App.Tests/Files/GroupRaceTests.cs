using DKNet.StaticData.App.Tests.FileGroups.Support;
using DKNet.StaticData.App.Tests.Files.Support;
using DKNet.StaticData.App.TestSupport;

namespace DKNet.StaticData.App.Tests.Files;

/// <summary>
/// Spec DRK-2235 §5 "Files and groups", races: a group deleted during an upload or a link that names it makes that call
/// answer 404, and a link saved first makes the group delete answer 409. The other call runs through the service's own
/// route, from <see cref="FilesApiFactory.BeforeSave"/>: after the first call's checks and before its save.
/// </summary>
public sealed class GroupRaceTests(FilesHost hosts) : IClassFixture<FilesHost>
{
    /// <summary>Scenario: A group deleted during an upload makes the upload fail cleanly.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task AGroupDeletedDuringAnUploadMakesTheUploadFailCleanly(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var groupId = await api.HasGroupAsync("customer-1", "Onboarding pack");
        var groupDeleted = DuringTheNextSave(api, () => api.DeleteGroupAsync(Callers.OnboardingSvc, "customer-1", groupId));

        using var response = await api.UploadIntoAsync(
            Callers.OnboardingSvc, "customer-1", "passport.pdf", FilesApi.NewKey(), groupId.ToString());

        await response.ShouldBeProblemDetailsAsync(HttpStatusCode.NotFound);
        using (var deleted = await groupDeleted)
        {
            await deleted.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        }

        (await api.FileNamesAsync("customer-1")).ShouldBeEmpty();
        api.StoredBlobs().ShouldBeEmpty();
    }

    /// <summary>Scenario: A group deleted during a link makes the link fail.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task AGroupDeletedDuringALinkMakesTheLinkFail(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var groupId = await api.HasGroupAsync("customer-1", "Onboarding pack");
        var fileId = await api.HasFileAsync("customer-1", "passport.pdf");
        var groupDeleted = DuringTheNextSave(api, () => api.DeleteGroupAsync(Callers.OnboardingSvc, "customer-1", groupId));

        using var response = await api.LinkAsync(Callers.OnboardingSvc, "customer-1", fileId, FileGroupsApi.LinkBody(groupId), "\"1\"");

        await response.ShouldBeProblemDetailsAsync(HttpStatusCode.NotFound);
        using (var deleted = await groupDeleted)
        {
            await deleted.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        }

        var details = await api.FileAsync("customer-1", fileId);
        details.Has("groupId").ShouldBeFalse();
        details.GetProperty("version").GetInt32().ShouldBe(1);
    }

    /// <summary>Scenario: A link saved first makes the group delete fail.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task ALinkSavedFirstMakesTheGroupDeleteFail(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var groupId = await api.HasGroupAsync("customer-1", "Onboarding pack");
        var fileId = await api.HasFileAsync("customer-1", "passport.pdf");
        var linked = DuringTheNextSave(api, () =>
            api.LinkAsync(Callers.OnboardingSvc, "customer-1", fileId, FileGroupsApi.LinkBody(groupId), "\"1\""));

        using var response = await api.DeleteGroupAsync(Callers.OnboardingSvc, "customer-1", groupId);

        await response.ShouldBeProblemDetailsAsync(HttpStatusCode.Conflict);
        using (var link = await linked)
        {
            await link.ShouldHaveStatusAsync(HttpStatusCode.OK);
        }

        (await api.GroupAsync("customer-1", groupId)).GetProperty("version").GetInt32().ShouldBe(1);
        (await api.FileAsync("customer-1", fileId)).GetProperty("groupId").GetGuid().ShouldBe(groupId);
    }

    /// <summary>
    /// Sends <paramref name="call"/> just before the service's next save and returns its answer. The answer task fails
    /// when no save comes within 30 seconds, so a call that never reached its save is reported by its own answer first.
    /// </summary>
    private static Task<HttpResponseMessage> DuringTheNextSave(FilesApi api, Func<Task<HttpResponseMessage>> call)
    {
        var answer = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        api.Host.BeforeSave.Run(async () => answer.SetResult(await call()));
        return answer.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }
}
