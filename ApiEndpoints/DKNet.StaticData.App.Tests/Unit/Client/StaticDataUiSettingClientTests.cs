using System.Text.Json;
using DKNet.StaticData.App.Tests.Client;
using DKNet.StaticData.Client;

namespace DKNet.StaticData.App.Tests.Unit.Client;

/// <summary>
/// The typed client's UI settings wire rules the client scenarios leave open (brief DRK-2248 §3, §6, §6a): each call's
/// route and verb, the version as <c>If-Match</c>, the bodies' left-out fields, the value sent and read as its exact
/// JSON text, a missing answer field read as empty and every error status as a typed error.
/// </summary>
public sealed class StaticDataUiSettingClientTests
{
    private const string BaseAddress = "https://staticdata.example.test";

    /// <summary>Brief DRK-2248 §6a D1.</summary>
    private const string ExactValue = """{ "pinned": null,  "width": 1.0, "label": "é" }""";

    private static readonly Guid SettingId = Guid.Parse("3b8f2d61-0c4e-4a7b-9e15-2f6a8c0d4e93");

    private static readonly Guid GroupId = Guid.Parse("7a1e4c55-2b9d-4e0f-8c3a-6d5f1b2e9a07");

    private static StaticDataFileClient ClientOver(HttpMessageHandler network) =>
        new(new HttpClient(network) { BaseAddress = new Uri(BaseAddress) });

    [Fact]
    public async Task ASettingCreateSendsTheValueAsItsOwnTextAndLeavesOutAnEmptyGroupAndSchemaVersion()
    {
        var network = new RecordingHandler
        {
            ResponseStatusCode = HttpStatusCode.Created,
            ResponseBody = $$"""{"settingId":"{{SettingId}}","owner":"a b&c","settingKey":"customers.list.table","settingType":"TableLayout","value":{{ExactValue}},"schemaVersion":1,"version":1,"createdBy":"layout-svc","createdOn":"2026-10-13T09:00:00+00:00"}"""
        };

        var setting = await ClientOver(network).CreateSettingAsync("a b&c", "customers.list.table", "TableLayout", ExactValue);

        var sent = network.LastRequest.ShouldNotBeNull();
        sent.Method.ShouldBe(HttpMethod.Post);
        sent.RequestUri.ShouldNotBeNull().AbsoluteUri.ShouldBe($"{BaseAddress}/v1/ui-settings?owner=a%20b%26c");
        sent.Headers.Contains("X-Idempotency-Key").ShouldBeFalse();
        sent.Content.ShouldNotBeNull().Headers.ContentType.ShouldNotBeNull().MediaType.ShouldBe("application/json");
        network.LastBody.ShouldBe(
            """{"settingKey":"customers.list.table","settingType":"TableLayout","value":{ "pinned": null,  "width": 1.0, "label": "é" }}""");
        setting.SettingId.ShouldBe(SettingId);
        setting.Owner.ShouldBe("a b&c");
        setting.SettingKey.ShouldBe("customers.list.table");
        setting.SettingType.ShouldBe("TableLayout");
        setting.Value.ShouldBe(ExactValue);
        setting.SchemaVersion.ShouldBe(1);
        setting.Version.ShouldBe(1);
        setting.CreatedBy.ShouldBe("layout-svc");
        setting.CreatedOn.ShouldBe(new DateTimeOffset(2026, 10, 13, 9, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task ASettingCreateSendsAGivenGroupAndSchemaVersion()
    {
        var network = new RecordingHandler { ResponseBody = $$"""{"groupId":"{{GroupId}}","schemaVersion":3}""" };

        var setting = await ClientOver(network).CreateSettingAsync(
            "backoffice-web", "customers.list.table", "TableLayout", "[1,2]", GroupId, schemaVersion: 3);

        network.LastBody.ShouldBe(
            $$"""{"settingKey":"customers.list.table","settingType":"TableLayout","groupId":"{{GroupId}}","value":[1,2],"schemaVersion":3}""");
        setting.GroupId.ShouldBe(GroupId);
        setting.SchemaVersion.ShouldBe(3);
    }

    [Theory]
    [InlineData("{\"columns\":")]
    [InlineData("not json")]
    [InlineData("1, \"settingType\": \"Other\"")]
    public async Task AValueThatIsNotOneJsonValueThrowsBeforeAnyRequest(string value)
    {
        var network = new RecordingHandler();
        var client = ClientOver(network);

        await Should.ThrowAsync<JsonException>(() =>
            client.CreateSettingAsync("backoffice-web", "customers.list.table", "TableLayout", value));
        await Should.ThrowAsync<JsonException>(() =>
            client.UpdateSettingAsync("backoffice-web", SettingId, version: 1, "TableLayout", value));

        network.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task ASettingUpdateSendsItsVersionAsIfMatchWithoutTheSettingKeyAndReturnsTheNewVersion()
    {
        var network = new RecordingHandler
        {
            ResponseBody = $$"""{"settingId":"{{SettingId}}","groupId":"{{GroupId}}","value":{"columns":["name"]},"schemaVersion":2,"version":3,"updatedBy":"layout-svc","updatedOn":"2026-10-13T10:00:00+00:00"}"""
        };

        var setting = await ClientOver(network).UpdateSettingAsync(
            "backoffice-web", SettingId, version: 2, "TableLayout", """{"columns":["name"]}""", GroupId, schemaVersion: 2);

        var sent = network.LastRequest.ShouldNotBeNull();
        sent.Method.ShouldBe(HttpMethod.Put);
        sent.RequestUri.ShouldNotBeNull().AbsoluteUri.ShouldBe($"{BaseAddress}/v1/ui-settings/{SettingId}?owner=backoffice-web");
        sent.Headers.GetValues("If-Match").ShouldBe(["\"2\""]);
        network.LastBody.ShouldBe(
            $$"""{"settingType":"TableLayout","groupId":"{{GroupId}}","value":{"columns":["name"]},"schemaVersion":2}""");
        setting.GroupId.ShouldBe(GroupId);
        setting.Value.ShouldBe("""{"columns":["name"]}""");
        setting.Version.ShouldBe(3);
        setting.UpdatedBy.ShouldBe("layout-svc");
        setting.UpdatedOn.ShouldBe(new DateTimeOffset(2026, 10, 13, 10, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task ASettingUpdateWithoutAGroupLeavesItOutSoTheSettingIsUnlinked()
    {
        var network = new RecordingHandler();

        await ClientOver(network).UpdateSettingAsync("backoffice-web", SettingId, version: 12, "TableLayout", "true");

        network.LastRequest.ShouldNotBeNull().Headers.GetValues("If-Match").ShouldBe(["\"12\""]);
        network.LastBody.ShouldBe("""{"settingType":"TableLayout","value":true}""");
    }

    [Fact]
    public async Task ASettingAnswerWithoutOptionalFieldsReadsThemAsEmpty()
    {
        var network = new RecordingHandler { ResponseBody = $$"""{"settingId":"{{SettingId}}","version":1}""" };

        var setting = await ClientOver(network).GetSettingAsync("backoffice-web", SettingId);

        setting.SettingId.ShouldBe(SettingId);
        setting.GroupId.ShouldBeNull();
        setting.Value.ShouldBeNull();
        setting.UpdatedBy.ShouldBeNull();
        setting.UpdatedOn.ShouldBeNull();
    }

    [Theory]
    [InlineData("""{"value":null}""", null)]
    [InlineData("""{"value":"text"}""", "\"text\"")]
    [InlineData("""{"value":[ 1,2 ]}""", "[ 1,2 ]")]
    [InlineData("""{"value":-0.50}""", "-0.50")]
    public async Task ASettingValueReadsAsItsExactJsonText(string answer, string? expected)
    {
        var network = new RecordingHandler { ResponseBody = answer };

        var setting = await ClientOver(network).GetSettingAsync("backoffice-web", SettingId);

        setting.Value.ShouldBe(expected);
    }

    [Fact]
    public async Task ASettingReadDeleteAndEraseCallTheirRoutes()
    {
        var network = new RecordingHandler();
        var client = ClientOver(network);

        await client.GetSettingAsync("backoffice-web", SettingId);
        await client.DeleteSettingAsync("backoffice-web", SettingId);
        await client.EraseSettingsAsync("backoffice-web");

        network.Requests.Select(r => $"{r.Method} {r.RequestUri!.AbsoluteUri}").ShouldBe(
        [
            $"GET {BaseAddress}/v1/ui-settings/{SettingId}?owner=backoffice-web",
            $"DELETE {BaseAddress}/v1/ui-settings/{SettingId}?owner=backoffice-web",
            $"DELETE {BaseAddress}/v1/ui-settings/all?owner=backoffice-web"
        ]);
    }

    [Fact]
    public async Task ASettingListSendsThePageDatesSettingKeyAndGroupEncoded()
    {
        var network = new RecordingHandler
        {
            ResponseBody = $$$"""{"items":[{"settingId":"{{{SettingId}}}","value":{"a":1}}],"pageNumber":2,"pageSize":5,"pageCount":2,"totalItemCount":6,"hasNextPage":false,"hasPreviousPage":true}"""
        };

        var page = await ClientOver(network).ListSettingsAsync("backoffice-web", pageNumber: 2, pageSize: 5,
            fromDate: new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
            toDate: new DateTimeOffset(2026, 6, 30, 23, 59, 59, TimeSpan.FromHours(7)),
            settingKey: "customers list&table", groupId: GroupId);

        network.LastRequest.ShouldNotBeNull().Method.ShouldBe(HttpMethod.Get);
        network.LastRequest.RequestUri.ShouldNotBeNull().AbsoluteUri.ShouldBe(
            $"{BaseAddress}/v1/ui-settings?owner=backoffice-web&pageNumber=2&pageSize=5"
            + "&fromDate=2026-06-01T00%3A00%3A00.0000000%2B00%3A00&toDate=2026-06-30T23%3A59%3A59.0000000%2B07%3A00"
            + $"&settingKey=customers%20list%26table&groupId={GroupId}");
        page.Items.Select(s => (s.SettingId, s.Value)).ShouldBe([(SettingId, """{"a":1}""")]);
        page.PageNumber.ShouldBe(2);
        page.PageSize.ShouldBe(5);
        page.PageCount.ShouldBe(2);
        page.TotalItemCount.ShouldBe(6);
        page.HasNextPage.ShouldBeFalse();
        page.HasPreviousPage.ShouldBeTrue();
    }

    [Fact]
    public async Task ASettingListWithoutFiltersSendsOnlyTheOwnerAndPage()
    {
        var network = new RecordingHandler();

        await ClientOver(network).ListSettingsAsync("backoffice-web", pageNumber: 1, pageSize: 10);

        network.LastRequest.ShouldNotBeNull().RequestUri.ShouldNotBeNull().AbsoluteUri
            .ShouldBe($"{BaseAddress}/v1/ui-settings?owner=backoffice-web&pageNumber=1&pageSize=10");
    }

    [Fact]
    public async Task ASettingGroupCreateLeavesOutAnEmptyDescriptionAndReadsAMissingOneAsEmpty()
    {
        var network = new RecordingHandler
        {
            ResponseStatusCode = HttpStatusCode.Created,
            ResponseBody = $$"""{"groupId":"{{GroupId}}","owner":"backoffice-web","name":"Customer screens","version":1,"createdBy":"layout-svc"}"""
        };

        var group = await ClientOver(network).CreateSettingGroupAsync("backoffice-web", "Customer screens");

        var sent = network.LastRequest.ShouldNotBeNull();
        sent.Method.ShouldBe(HttpMethod.Post);
        sent.RequestUri.ShouldNotBeNull().AbsoluteUri.ShouldBe($"{BaseAddress}/v1/ui-setting-groups?owner=backoffice-web");
        sent.Headers.Contains("X-Idempotency-Key").ShouldBeFalse();
        network.LastBody.ShouldBe("""{"name":"Customer screens"}""");
        group.GroupId.ShouldBe(GroupId);
        group.Owner.ShouldBe("backoffice-web");
        group.Name.ShouldBe("Customer screens");
        group.Version.ShouldBe(1);
        group.CreatedBy.ShouldBe("layout-svc");
        group.Description.ShouldBeNull();
        group.UpdatedBy.ShouldBeNull();
        group.UpdatedOn.ShouldBeNull();
    }

    [Fact]
    public async Task ASettingGroupCreateSendsAGivenDescriptionAndReadsItBack()
    {
        var network = new RecordingHandler { ResponseBody = """{"description":"Screens of the customer area"}""" };

        var group = await ClientOver(network).CreateSettingGroupAsync(
            "backoffice-web", "Customer screens", description: "Screens of the customer area");

        network.LastBody.ShouldBe("""{"name":"Customer screens","description":"Screens of the customer area"}""");
        group.Description.ShouldBe("Screens of the customer area");
    }

    [Fact]
    public async Task ASettingGroupUpdateSendsItsVersionAsIfMatchAndReturnsTheNewVersion()
    {
        var network = new RecordingHandler
        {
            ResponseBody = $$"""{"groupId":"{{GroupId}}","name":"Main screens","version":2,"updatedBy":"layout-svc","updatedOn":"2026-10-13T10:00:00+00:00"}"""
        };

        var group = await ClientOver(network).UpdateSettingGroupAsync(
            "backoffice-web", GroupId, version: 1, "Main screens", description: "Main");

        var sent = network.LastRequest.ShouldNotBeNull();
        sent.Method.ShouldBe(HttpMethod.Put);
        sent.RequestUri.ShouldNotBeNull().AbsoluteUri.ShouldBe($"{BaseAddress}/v1/ui-setting-groups/{GroupId}?owner=backoffice-web");
        sent.Headers.GetValues("If-Match").ShouldBe(["\"1\""]);
        network.LastBody.ShouldBe("""{"name":"Main screens","description":"Main"}""");
        group.Name.ShouldBe("Main screens");
        group.Version.ShouldBe(2);
        group.UpdatedBy.ShouldBe("layout-svc");
        group.UpdatedOn.ShouldBe(new DateTimeOffset(2026, 10, 13, 10, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task ASettingGroupReadListAndDeleteCallTheirRoutes()
    {
        var network = new RecordingHandler
        {
            ResponseBody = $$"""{"items":[{"groupId":"{{GroupId}}"}],"groupId":"{{GroupId}}","name":"Customer screens","totalItemCount":1}"""
        };
        var client = ClientOver(network);

        var group = await client.GetSettingGroupAsync("backoffice-web", GroupId);
        var page = await client.ListSettingGroupsAsync("backoffice-web", pageNumber: 3, pageSize: 7,
            fromDate: new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
            toDate: new DateTimeOffset(2026, 6, 30, 0, 0, 0, TimeSpan.Zero));
        await client.DeleteSettingGroupAsync("backoffice-web", GroupId);

        network.Requests.Select(r => $"{r.Method} {r.RequestUri!.AbsoluteUri}").ShouldBe(
        [
            $"GET {BaseAddress}/v1/ui-setting-groups/{GroupId}?owner=backoffice-web",
            $"GET {BaseAddress}/v1/ui-setting-groups?owner=backoffice-web&pageNumber=3&pageSize=7"
            + "&fromDate=2026-06-01T00%3A00%3A00.0000000%2B00%3A00&toDate=2026-06-30T00%3A00%3A00.0000000%2B00%3A00",
            $"DELETE {BaseAddress}/v1/ui-setting-groups/{GroupId}?owner=backoffice-web"
        ]);
        group.GroupId.ShouldBe(GroupId);
        group.Name.ShouldBe("Customer screens");
        page.Items.Select(g => g.GroupId).ShouldBe([GroupId]);
        page.TotalItemCount.ShouldBe(1);
    }

    private static readonly Dictionary<string, Func<StaticDataFileClient, Task>> Calls = new()
    {
        ["create setting"] = c => c.CreateSettingAsync("backoffice-web", "customers.list.table", "TableLayout", "{}"),
        ["list settings"] = c => c.ListSettingsAsync("backoffice-web", pageNumber: 1, pageSize: 10),
        ["read setting"] = c => c.GetSettingAsync("backoffice-web", SettingId),
        ["update setting"] = c => c.UpdateSettingAsync("backoffice-web", SettingId, version: 1, "TableLayout", "{}"),
        ["delete setting"] = c => c.DeleteSettingAsync("backoffice-web", SettingId),
        ["erase settings"] = c => c.EraseSettingsAsync("backoffice-web"),
        ["create group"] = c => c.CreateSettingGroupAsync("backoffice-web", "Customer screens"),
        ["list groups"] = c => c.ListSettingGroupsAsync("backoffice-web", pageNumber: 1, pageSize: 10),
        ["read group"] = c => c.GetSettingGroupAsync("backoffice-web", GroupId),
        ["update group"] = c => c.UpdateSettingGroupAsync("backoffice-web", GroupId, version: 1, "Main screens"),
        ["delete group"] = c => c.DeleteSettingGroupAsync("backoffice-web", GroupId)
    };

    /// <summary>Brief DRK-2248 §6a D2, plus 400 and 503, on each of the 11 calls.</summary>
    public static IEnumerable<object[]> Errors =>
        from call in Calls.Keys
        from status in new[] { 400, 404, 409, 412, 503 }
        select new object[] { call, (HttpStatusCode)status };

    [Theory]
    [MemberData(nameof(Errors))]
    public async Task EveryUiSettingsErrorBecomesATypedErrorWithItsStatusAndProblemDetails(string call, HttpStatusCode status)
    {
        var network = new RecordingHandler
        {
            ResponseStatusCode = status,
            ResponseBody = $$"""{"type":"https://tools.ietf.org/html/rfc9110","title":"Refused","status":{{(int)status}}}"""
        };

        var error = await Should.ThrowAsync<StaticDataApiException>(() => Calls[call](ClientOver(network)));

        error.StatusCode.ShouldBe(status);
        var details = error.ProblemDetails.ShouldNotBeNull();
        details.Status.ShouldBe((int)status);
        details.Title.ShouldBe("Refused");
    }
}
