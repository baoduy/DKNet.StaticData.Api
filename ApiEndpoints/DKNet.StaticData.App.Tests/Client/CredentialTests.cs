using DKNet.StaticData.Client;

namespace DKNet.StaticData.App.Tests.Client;

/// <summary>Spec DRK-2206 §5 "Client package": the client never gets, keeps or adds a credential of its own.</summary>
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
}
