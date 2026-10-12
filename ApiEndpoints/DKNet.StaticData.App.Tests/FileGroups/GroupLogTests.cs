using DKNet.StaticData.App.Tests.FileGroups.Support;
using DKNet.StaticData.App.Tests.Files.Support;
using DKNet.StaticData.App.TestSupport;
using Microsoft.Extensions.Logging;

namespace DKNet.StaticData.App.Tests.FileGroups;

/// <summary>
/// Spec DRK-2235 §5 "Logs": group create, update and delete, and file link and unlink, write no Information log entry,
/// and no log entry and no trace holds the owner, a file name, a group name or an external reference.
/// </summary>
/// <remarks>
/// "Writes no Information log entry" is read for the service's own code, the categories under <c>DKNet.StaticData</c>:
/// the framework's and the DKNet libraries' entries are theirs, not the action's.
/// </remarks>
public sealed class GroupLogTests(FilesHost hosts) : IClassFixture<FilesHost>
{
    /// <summary>Scenario: Group actions write no log entry and no personal data.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task GroupActionsWriteNoLogEntryAndNoPersonalData(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var fileId = await api.HasFileAsync("customer-1", "passport.pdf");
        api.Host.LogCapture.Clear();
        using var traces = new TestTraceCapture();

        Guid groupId;
        using (var created = await api.CreateGroupAsync(Callers.OnboardingSvc, "customer-1",
                   FileGroupsApi.GroupBody("Onboarding pack", externalRef: "CUS-0042"), FilesApi.NewKey()))
        {
            await created.ShouldHaveStatusAsync(HttpStatusCode.Created);
            groupId = (await created.JsonAsync()).GetProperty("groupId").GetGuid();
        }

        await ExpectAsync(api.UpdateGroupAsync(Callers.OnboardingSvc, "customer-1", groupId,
            FileGroupsApi.GroupBody("Pack 2", externalRef: "CUS-0042"), "\"1\""), HttpStatusCode.OK);
        await ExpectAsync(api.LinkAsync(Callers.OnboardingSvc, "customer-1", fileId, FileGroupsApi.LinkBody(groupId), "\"1\""),
            HttpStatusCode.OK);
        await ExpectAsync(api.LinkAsync(Callers.OnboardingSvc, "customer-1", fileId, FileGroupsApi.UnlinkBody, "\"2\""),
            HttpStatusCode.OK);
        await ExpectAsync(api.DeleteGroupAsync(Callers.OnboardingSvc, "customer-1", groupId), HttpStatusCode.NoContent);

        var entries = api.Host.LogCapture.Entries;
        entries.ShouldNotContain(
            e => e.Level == LogLevel.Information && e.Category.StartsWith("DKNet.StaticData", StringComparison.Ordinal),
            "a group or link action wrote an Information log entry");
        foreach (var personal in new[] { "customer-1", "Onboarding pack", "CUS-0042", "passport.pdf" })
        {
            entries.ShouldNotContain(e => e.Holds(personal), $"a log entry holds \"{personal}\"");
            traces.Holds(personal).ShouldBeFalse($"a trace holds \"{personal}\"");
        }
    }

    private static async Task ExpectAsync(Task<HttpResponseMessage> call, HttpStatusCode status)
    {
        using var response = await call;
        await response.ShouldHaveStatusAsync(status);
    }
}
