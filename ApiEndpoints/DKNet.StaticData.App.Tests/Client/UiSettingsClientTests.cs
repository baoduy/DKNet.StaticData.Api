using DKNet.StaticData.App.Tests.Files.Support;
using DKNet.StaticData.App.Tests.SettingGroups.Support;
using DKNet.StaticData.App.Tests.UiSettings;
using DKNet.StaticData.App.Tests.UiSettings.Support;
using DKNet.StaticData.App.TestSupport;
using DKNet.StaticData.Client;

namespace DKNet.StaticData.App.Tests.Client;

/// <summary>
/// Spec DRK-2245 §5 "Client package", against the service itself: "layout-svc" registers
/// <see cref="IStaticDataFileClient"/> with its own token handler and makes the 11 UI settings calls through it; the
/// value comes back exactly as the service answered it and every error answer becomes a typed error.
/// </summary>
public sealed class UiSettingsClientTests(FilesHost hosts) : IClassFixture<FilesHost>
{
    private const string NewValue = """{"columns":["name"],"sort":"name","pageSize":25}""";

    /// <summary>Scenario: A calling service saves a layout through the typed client.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task ACallingServiceSavesALayoutThroughTheTypedClient(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        using var services = TypedClientTests.ClientOf(api, Callers.LayoutSvc);
        var client = services.GetRequiredService<IStaticDataFileClient>();

        var group = await client.CreateSettingGroupAsync("backoffice-web", "Customer screens");
        var created = await client.CreateSettingAsync(
            "backoffice-web", "customers.list.table", "TableLayout", UiSettingsApi.TableLayout, group.GroupId);
        var listed = await client.ListSettingsAsync("backoffice-web", pageNumber: 1, pageSize: 10, settingKey: "customers.list.table");
        var read = await client.GetSettingAsync("backoffice-web", created.SettingId);
        var updated = await client.UpdateSettingAsync(
            "backoffice-web", read.SettingId, read.Version, "TableLayout", NewValue, group.GroupId);
        var groups = await client.ListSettingGroupsAsync("backoffice-web", pageNumber: 1, pageSize: 10);
        var readGroup = await client.GetSettingGroupAsync("backoffice-web", group.GroupId);
        var renamed = await client.UpdateSettingGroupAsync("backoffice-web", group.GroupId, readGroup.Version, "Main screens");
        await client.DeleteSettingAsync("backoffice-web", created.SettingId);
        await client.DeleteSettingGroupAsync("backoffice-web", group.GroupId);
        await client.EraseSettingsAsync("backoffice-web");

        group.Name.ShouldBe("Customer screens");
        group.Owner.ShouldBe("backoffice-web");
        group.Version.ShouldBe(1);
        created.SettingKey.ShouldBe("customers.list.table");
        created.GroupId.ShouldBe(group.GroupId);
        created.Value.ShouldBe(UiSettingsApi.TableLayout);
        created.Version.ShouldBe(1);
        listed.Items.Select(s => s.SettingId).ShouldBe([created.SettingId]);
        read.Value.ShouldBe(UiSettingsApi.TableLayout);
        read.CreatedBy.ShouldBe("layout-svc");
        updated.Version.ShouldBe(2); // each update call returns the new version
        updated.Value.ShouldBe(NewValue);
        groups.Items.Select(g => g.GroupId).ShouldBe([group.GroupId]);
        readGroup.Description.ShouldBeNullOrEmpty(); // the client reads the missing description field as empty
        renamed.Name.ShouldBe("Main screens");
        renamed.Version.ShouldBe(2);
        (await api.SettingKeysAsync("backoffice-web")).ShouldBeEmpty();
        (await api.SettingGroupNamesAsync("backoffice-web")).ShouldBeEmpty();
    }

    /// <summary>Scenario: The typed client returns the value exactly as the service answers it.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task TheTypedClientReturnsTheValueExactlyAsTheServiceAnswersIt(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var settingId = await api.HasSettingAsync("backoffice-web", "customers.list.table", value: CreateSettingTests.ExactValue);
        using var services = TypedClientTests.ClientOf(api, Callers.LayoutSvc);
        var client = services.GetRequiredService<IStaticDataFileClient>();

        var setting = await client.GetSettingAsync("backoffice-web", settingId);

        setting.Value.ShouldBe("""{ "pinned": null,  "width": 1.0, "label": "é" }""");
    }

    public static IEnumerable<object[]> SettingErrors => Databases.With(
        ["updates \"customers.list.table\" with the version 1", HttpStatusCode.PreconditionFailed],
        ["creates \"customers.list.table\" again", HttpStatusCode.Conflict],
        ["deletes \"Customer screens\"", HttpStatusCode.Conflict],
        ["reads an unknown setting for owner \"backoffice-web\"", HttpStatusCode.NotFound]);

    /// <summary>Scenario Outline: The typed client turns a UI settings error into a typed error.</summary>
    [Theory]
    [MemberData(nameof(SettingErrors))]
    public async Task TheTypedClientTurnsAUiSettingsErrorIntoATypedError(TestDatabase database, string action, HttpStatusCode status)
    {
        var api = await hosts.OnAsync(database);
        var groupId = await api.HasSettingGroupAsync("backoffice-web", "Customer screens");
        var settingId = await api.HasSettingAsync("backoffice-web", "customers.list.table", groupId);
        await api.RaiseSettingToVersion2Async("backoffice-web", settingId, groupId);
        using var services = TypedClientTests.ClientOf(api, Callers.LayoutSvc);
        var client = services.GetRequiredService<IStaticDataFileClient>();

        Func<Task> call = action switch
        {
            "updates \"customers.list.table\" with the version 1" => () => client.UpdateSettingAsync(
                "backoffice-web", settingId, version: 1, "TableLayout", NewValue, groupId),
            "creates \"customers.list.table\" again" => () => client.CreateSettingAsync(
                "backoffice-web", "customers.list.table", "TableLayout", UiSettingsApi.TableLayout),
            "deletes \"Customer screens\"" => () => client.DeleteSettingGroupAsync("backoffice-web", groupId),
            "reads an unknown setting for owner \"backoffice-web\"" => () => client.GetSettingAsync("backoffice-web", Guid.NewGuid()),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        };

        var error = await Should.ThrowAsync<StaticDataApiException>(call);

        error.StatusCode.ShouldBe(status);
        error.ProblemDetails.ShouldNotBeNull().Status.ShouldBe((int)status);
    }
}
