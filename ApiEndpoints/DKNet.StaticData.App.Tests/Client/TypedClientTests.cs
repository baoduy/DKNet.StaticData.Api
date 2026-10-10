using System.Net.Http.Headers;
using DKNet.StaticData.App.Tests.Files.Support;
using DKNet.StaticData.App.TestSupport;
using DKNet.StaticData.Client;

namespace DKNet.StaticData.App.Tests.Client;

/// <summary>
/// Spec DRK-2206 §5 "Client package", against the service itself: a calling service registers
/// <see cref="IStaticDataFileClient"/> with its own token handler and calls every file route through it.
/// </summary>
public sealed class TypedClientTests(FilesHost hosts) : IClassFixture<FilesHost>
{
    /// <summary>Scenario: A calling service uses the typed client for every file route.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task ACallingServiceUsesTheTypedClientForEveryFileRoute(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        using var services = ClientOf(api, Callers.OnboardingSvc);
        var client = services.GetRequiredService<IStaticDataFileClient>();
        var bytes = Bytes.Of(1024);

        var uploaded = await client.UploadAsync("customer-1", "upload-1", "passport.pdf", new MemoryStream(bytes));
        var page = await client.ListAsync("customer-1", pageNumber: 1, pageSize: 10);
        var read = await client.GetAsync("customer-1", uploaded.FileId);
        await using var downloaded = await client.DownloadAsync("customer-1", uploaded.FileId);
        using var content = new MemoryStream();
        await downloaded.Content.CopyToAsync(content);
        await client.DeleteAsync("customer-1", uploaded.FileId);

        uploaded.FileName.ShouldBe("passport.pdf");
        uploaded.Owner.ShouldBe("customer-1");
        page.Items.Select(f => f.FileId).ShouldBe([uploaded.FileId]);
        read.FileId.ShouldBe(uploaded.FileId);
        read.CreatedBy.ShouldBe("onboarding-svc");
        read.UpdatedBy.ShouldBeNullOrEmpty(); // the client reads the missing updated-by field as empty
        downloaded.FileName.ShouldBe("passport.pdf");
        downloaded.ContentType.ShouldBe("application/pdf");
        content.ToArray().ShouldBe(bytes);
        (await api.FileNamesAsync("customer-1")).ShouldBeEmpty();
    }

    /// <summary>Scenario: The typed client turns an error answer into a typed error.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task TheTypedClientTurnsAnErrorAnswerIntoATypedError(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        using var services = ClientOf(api, Callers.OnboardingSvc);
        var client = services.GetRequiredService<IStaticDataFileClient>();

        var error = await Should.ThrowAsync<StaticDataApiException>(() => client.GetAsync("customer-1", Guid.NewGuid()));

        error.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        error.ProblemDetails.ShouldNotBeNull().Status.ShouldBe(404);
    }

    /// <summary>
    /// The calling service's container: the typed client registered with the service's address and the calling
    /// service's own <see cref="BearerTokenHandler"/>, sending through the test host's in-memory transport.
    /// </summary>
    private static ServiceProvider ClientOf(FilesApi api, string token)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new BearerTokenHandler(token));
        services.AddStaticDataClient(api.Host.Server.BaseAddress, typeof(BearerTokenHandler));
        services.AddHttpClient<IStaticDataFileClient, StaticDataFileClient>()
            .ConfigurePrimaryHttpMessageHandler(() => api.Host.Server.CreateHandler());
        return services.BuildServiceProvider();
    }

    /// <summary>The calling service's own credential handler: attaches its bearer token to every request.</summary>
    private sealed class BearerTokenHandler(string token) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return base.SendAsync(request, cancellationToken);
        }
    }
}
