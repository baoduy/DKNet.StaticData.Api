using DKNet.StaticData.App.Tests.FileGroups.Support;
using DKNet.StaticData.App.Tests.Files.Support;
using DKNet.StaticData.App.TestSupport;

namespace DKNet.StaticData.App.Tests.FileGroups;

/// <summary>
/// Spec DRK-2235 §5 "Delete a group": a group with no linked file is deleted with 204, one with a linked file answers
/// 409 and changes nothing, and a version tag sent with a delete is ignored.
/// </summary>
public sealed class DeleteGroupTests(FilesHost hosts) : IClassFixture<FilesHost>
{
    /// <summary>Scenario: A caller deletes a group with no linked file.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task ACallerDeletesAGroupWithNoLinkedFile(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var groupId = await api.HasGroupAsync("customer-1", "Onboarding pack");
        (await api.GroupNamesAsync("customer-1")).ShouldBe(["Onboarding pack"]);

        using var response = await api.DeleteGroupAsync(Callers.OnboardingSvc, "customer-1", groupId);

        await response.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        using var read = await api.ReadGroupAsync(Callers.OnboardingSvc, "customer-1", groupId);
        await read.ShouldBeProblemDetailsAsync(HttpStatusCode.NotFound);
        (await api.GroupNamesAsync("customer-1")).ShouldBeEmpty();
    }

    /// <summary>Scenario Outline: A group with a linked file is not deleted.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task AGroupWithALinkedFileIsNotDeleted(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var groupId = await api.HasGroupAsync("customer-1", "Onboarding pack");
        var fileId = await api.HasFileInGroupAsync("customer-1", "passport.pdf", groupId);

        using var response = await api.DeleteGroupAsync(Callers.OnboardingSvc, "customer-1", groupId);

        await response.ShouldBeProblemDetailsAsync(HttpStatusCode.Conflict);
        (await api.GroupAsync("customer-1", groupId)).GetProperty("version").GetInt32().ShouldBe(1);
        (await api.FileAsync("customer-1", fileId)).GetProperty("groupId").GetGuid().ShouldBe(groupId);
    }

    /// <summary>Scenario: A group delete ignores a version tag.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task AGroupDeleteIgnoresAVersionTag(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var groupId = await api.HasGroupAsync("customer-1", "Onboarding pack");
        (await api.GroupNamesAsync("customer-1")).ShouldBe(["Onboarding pack"]);

        using var response = await api.DeleteGroupAsync(Callers.OnboardingSvc, "customer-1", groupId, ifMatch: "\"7\"");

        await response.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        (await api.GroupNamesAsync("customer-1")).ShouldBeEmpty();
    }

    /// <summary>Scenario: Deleting the last linked file lets the group be deleted.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task DeletingTheLastLinkedFileLetsTheGroupBeDeleted(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var groupId = await api.HasGroupAsync("customer-1", "Onboarding pack");
        var fileId = await api.HasFileInGroupAsync("customer-1", "passport.pdf", groupId);
        using (var deleted = await api.DeleteAsync(Callers.OnboardingSvc, "customer-1", fileId))
        {
            await deleted.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        }

        using var response = await api.DeleteGroupAsync(Callers.OnboardingSvc, "customer-1", groupId);

        await response.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        (await api.GroupNamesAsync("customer-1")).ShouldBeEmpty();
    }
}
