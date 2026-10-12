using System.Net.Http.Headers;
using DKNet.StaticData.App.Tests.FileGroups.Support;
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

    /// <summary>Scenario: A calling service uses the typed client for every group route (spec DRK-2235 §5).</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task ACallingServiceUsesTheTypedClientForEveryGroupRoute(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        using var services = ClientOf(api, Callers.OnboardingSvc);
        var client = services.GetRequiredService<IStaticDataFileClient>();

        var created = await client.CreateGroupAsync("customer-1", "group-1", "Onboarding pack");
        var uploaded = await client.UploadAsync(
            "customer-1", "upload-1", "passport.pdf", new MemoryStream(Bytes.Of(1024)), groupId: created.GroupId);
        var groupFiles = await client.ListAsync("customer-1", pageNumber: 1, pageSize: 10, groupId: created.GroupId);
        var renamed = await client.UpdateGroupAsync("customer-1", created.GroupId, created.Version, "Onboarding pack 2");
        var unlinked = await client.LinkAsync("customer-1", uploaded.FileId, groupId: null, uploaded.Version);
        await client.DeleteGroupAsync("customer-1", created.GroupId);

        created.Name.ShouldBe("Onboarding pack");
        created.Owner.ShouldBe("customer-1");
        created.Version.ShouldBe(1);
        created.Purpose.ShouldBeNullOrEmpty(); // the client reads the missing purpose field as empty
        uploaded.GroupId.ShouldBe(created.GroupId);
        groupFiles.Items.Select(f => f.FileId).ShouldBe([uploaded.FileId]);
        renamed.Name.ShouldBe("Onboarding pack 2");
        renamed.Version.ShouldBe(2);
        unlinked.GroupId.ShouldBeNull();
        unlinked.Version.ShouldBe(2);
        (await api.GroupNamesAsync("customer-1")).ShouldBeEmpty();
    }

    public static IEnumerable<object[]> GroupErrors => Databases.With(
        ["renames \"Onboarding pack\" with the version 1", HttpStatusCode.PreconditionFailed],
        ["deletes \"Onboarding pack\"", HttpStatusCode.Conflict],
        ["reads an unknown group for owner \"customer-1\"", HttpStatusCode.NotFound]);

    /// <summary>Scenario Outline: The typed client turns a group error into a typed error (spec DRK-2235 §5).</summary>
    [Theory]
    [MemberData(nameof(GroupErrors))]
    public async Task TheTypedClientTurnsAGroupErrorIntoATypedError(TestDatabase database, string action, HttpStatusCode status)
    {
        var api = await hosts.OnAsync(database);
        var groupId = await api.HasGroupAsync("customer-1", "Onboarding pack");
        await api.RaiseToVersion2Async("customer-1", groupId, "Onboarding pack");
        await api.HasFileInGroupAsync("customer-1", "passport.pdf", groupId);
        using var services = ClientOf(api, Callers.OnboardingSvc);
        var client = services.GetRequiredService<IStaticDataFileClient>();

        Func<Task> call = action switch
        {
            "renames \"Onboarding pack\" with the version 1" => () =>
                client.UpdateGroupAsync("customer-1", groupId, version: 1, "Pack 2"),
            "deletes \"Onboarding pack\"" => () => client.DeleteGroupAsync("customer-1", groupId),
            "reads an unknown group for owner \"customer-1\"" => () => client.GetGroupAsync("customer-1", Guid.NewGuid()),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        };

        var error = await Should.ThrowAsync<StaticDataApiException>(call);

        error.StatusCode.ShouldBe(status);
        error.ProblemDetails.ShouldNotBeNull().Status.ShouldBe((int)status);
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
