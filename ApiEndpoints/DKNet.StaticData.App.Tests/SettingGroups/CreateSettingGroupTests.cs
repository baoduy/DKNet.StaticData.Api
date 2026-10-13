using DKNet.StaticData.App.Tests.FileGroups.Support;
using DKNet.StaticData.App.Tests.Files.Support;
using DKNet.StaticData.App.Tests.SettingGroups.Support;
using DKNet.StaticData.App.Tests.UiSettings.Support;
using DKNet.StaticData.App.TestSupport;

namespace DKNet.StaticData.App.Tests.SettingGroups;

/// <summary>
/// Spec DRK-2245 §5 "Setting groups": a create answers 201 with its location and version tag; the name and the
/// description follow the display text rule; a name the owner already uses answers 409 on create and on rename, and
/// names compare exactly.
/// </summary>
public sealed class CreateSettingGroupTests(FilesHost hosts) : IClassFixture<FilesHost>
{
    /// <summary>Scenario Outline: A caller creates a setting group.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task ACallerCreatesASettingGroup(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);

        using var response = await api.CreateSettingGroupAsync(Callers.LayoutSvc, "backoffice-web",
            SettingGroupsApi.GroupBody("Customer screens"));

        await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
        var group = await response.JsonAsync();
        var groupId = group.GetProperty("groupId").GetGuid();
        var location = response.Headers.Location.ShouldNotBeNull();
        (location.IsAbsoluteUri ? location.PathAndQuery : location.OriginalString)
            .ShouldBe($"/v1/ui-setting-groups/{groupId}?owner=backoffice-web");
        response.Headers.ETag.ShouldNotBeNull("the answer carries no ETag").Tag.ShouldBe("\"1\"");
        group.GetProperty("name").GetString().ShouldBe("Customer screens");
        group.GetProperty("owner").GetString().ShouldBe("backoffice-web");
        group.GetProperty("version").GetInt32().ShouldBe(1);
        group.GetProperty("createdBy").GetString().ShouldBe("layout-svc");
        group.Has("description").ShouldBeFalse();
        group.Has("updatedBy").ShouldBeFalse();
        group.Has("updatedOn").ShouldBeFalse();
    }

    public static IEnumerable<object[]> DisplayTexts => Databases.With(
        ["the name \"Onboarding pack 2026\"", HttpStatusCode.Created],
        ["the name \"Pack-2\" and the description \"my_layouts\"", HttpStatusCode.Created],
        ["the name \"Café\"", HttpStatusCode.BadRequest],
        ["the name \"Pack, v2.\"", HttpStatusCode.BadRequest],
        ["the name \"Pack/2\"", HttpStatusCode.BadRequest],
        ["the name \"Pack\" and the description \"Main screens.\"", HttpStatusCode.BadRequest],
        ["no name", HttpStatusCode.BadRequest],
        ["the name \"   \"", HttpStatusCode.BadRequest],
        ["a name of 101 characters", HttpStatusCode.BadRequest],
        ["the name \"Pack\" and the description \"\"", HttpStatusCode.BadRequest],
        ["the name \"Pack\" and the description \"   \"", HttpStatusCode.BadRequest],
        ["the name \"Pack\" and a description of 501 characters", HttpStatusCode.BadRequest],
        ["the name \"Pack\" and the description null", HttpStatusCode.Created]);

    /// <summary>Scenario Outline: Setting group texts follow the display text rule.</summary>
    [Theory]
    [MemberData(nameof(DisplayTexts))]
    public async Task SettingGroupTextsFollowTheDisplayTextRule(TestDatabase database, string fields, HttpStatusCode status)
    {
        var api = await hosts.OnAsync(database);
        (string Body, string? Name, string? Description) sent = fields switch
        {
            "the name \"Onboarding pack 2026\"" => (SettingGroupsApi.GroupBody("Onboarding pack 2026"), "Onboarding pack 2026", null),
            "the name \"Pack-2\" and the description \"my_layouts\"" =>
                (SettingGroupsApi.GroupBody("Pack-2", "my_layouts"), "Pack-2", "my_layouts"),
            "the name \"Café\"" => (SettingGroupsApi.GroupBody("Café"), null, null),
            "the name \"Pack, v2.\"" => (SettingGroupsApi.GroupBody("Pack, v2."), null, null),
            "the name \"Pack/2\"" => (SettingGroupsApi.GroupBody("Pack/2"), null, null),
            "the name \"Pack\" and the description \"Main screens.\"" => (SettingGroupsApi.GroupBody("Pack", "Main screens."), null, null),
            "no name" => (SettingGroupsApi.GroupBody(null), null, null),
            "the name \"   \"" => (SettingGroupsApi.GroupBody("   "), null, null),
            "a name of 101 characters" => (SettingGroupsApi.GroupBody(new string('n', 101)), null, null),
            "the name \"Pack\" and the description \"\"" => (SettingGroupsApi.GroupBody("Pack", ""), null, null),
            "the name \"Pack\" and the description \"   \"" => (SettingGroupsApi.GroupBody("Pack", "   "), null, null),
            "the name \"Pack\" and a description of 501 characters" =>
                (SettingGroupsApi.GroupBody("Pack", new string('d', 501)), null, null),
            "the name \"Pack\" and the description null" => ("""{"name":"Pack","description":null}""", "Pack", null),
            _ => throw new ArgumentOutOfRangeException(nameof(fields), fields, null)
        };

        using var response = await api.CreateSettingGroupAsync(Callers.LayoutSvc, "backoffice-web", sent.Body);

        if (status == HttpStatusCode.Created)
        {
            await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
            var group = await api.SettingGroupAsync("backoffice-web", (await response.JsonAsync()).GetProperty("groupId").GetGuid());
            group.GetProperty("name").GetString().ShouldBe(sent.Name);
            if (sent.Description is null)
            {
                group.Has("description").ShouldBeFalse();
            }
            else
            {
                group.GetProperty("description").GetString().ShouldBe(sent.Description);
            }
        }
        else
        {
            await response.ShouldBeProblemDetailsAsync(status);
            (await api.SettingGroupNamesAsync("backoffice-web")).ShouldBeEmpty();
        }
    }

    public static IEnumerable<object[]> UsedNames => Databases.With(
        ["creates the group \"Customer screens\"", "409 as problem details that say the name is already used"],
        ["renames \"Order screens\" to \"Customer screens\"", "409, and \"Order screens\" is unchanged"],
        ["creates the group \"customer screens\"", "201"]);

    /// <summary>Scenario Outline: A name the owner already uses is refused on create and on rename.</summary>
    [Theory]
    [MemberData(nameof(UsedNames))]
    public async Task ANameTheOwnerAlreadyUsesIsRefusedOnCreateAndOnRename(TestDatabase database, string action, string status)
    {
        var api = await hosts.OnAsync(database);
        await api.HasSettingGroupAsync("backoffice-web", "Customer screens");
        var orderScreens = await api.HasSettingGroupAsync("backoffice-web", "Order screens");

        using var response = action switch
        {
            "creates the group \"Customer screens\"" => await api.CreateSettingGroupAsync(
                Callers.LayoutSvc, "backoffice-web", SettingGroupsApi.GroupBody("Customer screens")),
            "renames \"Order screens\" to \"Customer screens\"" => await api.UpdateSettingGroupAsync(
                Callers.LayoutSvc, "backoffice-web", orderScreens, SettingGroupsApi.GroupBody("Customer screens"), "\"1\""),
            "creates the group \"customer screens\"" => await api.CreateSettingGroupAsync(
                Callers.LayoutSvc, "backoffice-web", SettingGroupsApi.GroupBody("customer screens")),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        };

        switch (status)
        {
            case "409 as problem details that say the name is already used":
                await response.ShouldBeProblemDetailsAsync(HttpStatusCode.Conflict);
                (await response.JsonAsync()).GetProperty("detail").GetString().ShouldNotBeNull()
                    .ShouldContain("already used", Case.Insensitive, "the 409 does not say the name is already used");
                (await api.SettingGroupNamesAsync("backoffice-web")).ShouldBe(["Order screens", "Customer screens"]);
                break;
            case "409, and \"Order screens\" is unchanged":
                await response.ShouldBeProblemDetailsAsync(HttpStatusCode.Conflict);
                var group = await api.SettingGroupAsync("backoffice-web", orderScreens);
                group.GetProperty("name").GetString().ShouldBe("Order screens");
                group.GetProperty("version").GetInt32().ShouldBe(1);
                break;
            default:
                await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
                (await api.SettingGroupNamesAsync("backoffice-web"))
                    .ShouldBe(["customer screens", "Order screens", "Customer screens"]);
                break;
        }
    }

    /// <summary>Scenario Outline: Two creates of one group name at the same moment make 1 group.</summary>
    /// <remarks>"At the same moment": each create is held just before its save until both have made their checks.</remarks>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task TwoCreatesOfOneGroupNameAtTheSameMomentMake1Group(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var body = SettingGroupsApi.GroupBody("Customer screens");
        SaveRaces.HoldBothSavesTogether(api);

        var answers = await Task.WhenAll(
            api.CreateSettingGroupAsync(Callers.LayoutSvc, "backoffice-web", body),
            api.CreateSettingGroupAsync(Callers.LayoutSvc, "backoffice-web", body));

        try
        {
            await SaveRaces.ShouldBePairAsync(answers, HttpStatusCode.Created, HttpStatusCode.Conflict);
        }
        finally
        {
            foreach (var answer in answers)
            {
                answer.Dispose();
            }
        }

        (await api.SettingGroupNamesAsync("backoffice-web")).ShouldBe(["Customer screens"]);
    }
}
