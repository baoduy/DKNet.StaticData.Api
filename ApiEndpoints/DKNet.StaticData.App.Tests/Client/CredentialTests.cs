using DKNet.StaticData.App.Tests.Files.Support;
using DKNet.StaticData.App.TestSupport;
using DKNet.StaticData.Client;
using Microsoft.Extensions.Logging;

namespace DKNet.StaticData.App.Tests.Client;

/// <summary>
/// Spec DRK-2206 §5 and spec DRK-2245 §5 "Client package": the client never gets, keeps or adds a credential of its own.
/// </summary>
public sealed class CredentialTests
{
    private const string EmptyPage =
        """{"items":[],"pageCount":0,"pageNumber":1,"pageSize":10,"totalItemCount":0,"hasNextPage":false,"hasPreviousPage":false}""";

    /// <summary>Scenario: The typed client adds no credential of its own.</summary>
    [Fact]
    public async Task TheTypedClientAddsNoCredentialOfItsOwn()
    {
        var network = new RecordingHandler { ResponseBody = EmptyPage };
        var services = new ServiceCollection();
        services.AddStaticDataClient(new Uri("https://staticdata.example.test"));
        // only the primary transport is swapped, so the test never reaches a socket; no message handler is added
        services.AddHttpClient<IStaticDataFileClient, StaticDataFileClient>().ConfigurePrimaryHttpMessageHandler(() => network);
        await using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<IStaticDataFileClient>().ListAsync("customer-1", pageNumber: 1, pageSize: 10);

        var request = network.LastRequest.ShouldNotBeNull("the list call never reached the network");
        request.Headers.Authorization.ShouldBeNull();
        request.Headers.Select(h => h.Key).ShouldNotContain(h => h.Equals("Cookie", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Scenario: The typed client never handles a credential on the UI settings calls.</summary>
    [Fact]
    public async Task TheTypedClientNeverHandlesACredentialOnTheUiSettingsCalls()
    {
        var token = Callers.LayoutSvc;
        var id = Guid.Parse("0d6f0c2a-5b8e-4f43-9a51-3f0f4c8f2b11");
        // one answer every call can read: a setting, a group and an empty page
        var network = new RecordingHandler
        {
            ResponseBody = $$"""{"settingId":"{{id}}","groupId":"{{id}}","value":{"columns":[]},"version":2,"items":[],"pageNumber":1,"pageSize":10}"""
        };
        var logs = new TestLogCapture();
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddProvider(logs));
        // "layout-svc" attaches its own token through its own handler
        services.AddSingleton(new TypedClientTests.BearerTokenHandler(token));
        services.AddStaticDataClient(new Uri("https://staticdata.example.test"), typeof(TypedClientTests.BearerTokenHandler));
        services.AddHttpClient<IStaticDataFileClient, StaticDataFileClient>().ConfigurePrimaryHttpMessageHandler(() => network);
        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IStaticDataFileClient>();

        await client.CreateSettingGroupAsync("backoffice-web", "Customer screens");
        await client.CreateSettingAsync("backoffice-web", "customers.list.table", "TableLayout", """{"columns":[]}""", id);
        await client.ListSettingsAsync("backoffice-web", pageNumber: 1, pageSize: 10, settingKey: "customers.list.table");
        await client.GetSettingAsync("backoffice-web", id);
        await client.UpdateSettingAsync("backoffice-web", id, version: 1, "TableLayout", """{"columns":["name"]}""", id);
        await client.ListSettingGroupsAsync("backoffice-web", pageNumber: 1, pageSize: 10);
        await client.GetSettingGroupAsync("backoffice-web", id);
        await client.UpdateSettingGroupAsync("backoffice-web", id, version: 1, "Main screens");
        await client.DeleteSettingAsync("backoffice-web", id);
        await client.DeleteSettingGroupAsync("backoffice-web", id);
        await client.EraseSettingsAsync("backoffice-web");

        network.Requests.Count.ShouldBe(11);
        foreach (var request in network.Requests)
        {
            var authorization = request.Headers.Authorization.ShouldNotBeNull($"{request.Method} {request.RequestUri} carries no token");
            authorization.Scheme.ShouldBe("Bearer");
            authorization.Parameter.ShouldBe(token);
            request.Headers.GetValues("Authorization").ShouldHaveSingleItem();
            request.Headers.Select(h => h.Key).ShouldNotContain(h => h.Equals("Cookie", StringComparison.OrdinalIgnoreCase));
        }

        // the client keeps no copy of the token
        foreach (var field in client.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
                                                         System.Reflection.BindingFlags.NonPublic))
        {
            switch (field.GetValue(client))
            {
                case string text:
                    text.ShouldNotContain(token, Case.Sensitive, $"the client's {field.Name} holds the token");
                    break;
                case HttpClient http:
                    http.DefaultRequestHeaders.Authorization.ShouldBeNull($"the client's {field.Name} holds a credential");
                    break;
            }
        }

        logs.Entries.ShouldNotContain(e => e.Holds(token), "a log entry holds the token");
    }
}
