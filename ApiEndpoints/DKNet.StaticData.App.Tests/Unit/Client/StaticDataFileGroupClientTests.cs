using DKNet.StaticData.App.Tests.Client;
using DKNet.StaticData.Client;

namespace DKNet.StaticData.App.Tests.Unit.Client;

/// <summary>
/// The typed client's group and link wire rules the client scenarios leave open (brief DRK-2238 §6a): each call's
/// route and verb, the version as <c>If-Match</c>, the bodies' left-out and present-null fields, the group list query
/// and every error status as a typed error.
/// </summary>
public sealed class StaticDataFileGroupClientTests
{
    private const string BaseAddress = "https://staticdata.example.test";

    private static readonly Guid FileId = Guid.Parse("0d6f0c2a-5b8e-4f43-9a51-3f0f4c8f2b11");

    private static readonly Guid GroupId = Guid.Parse("7a1e4c55-2b9d-4e0f-8c3a-6d5f1b2e9a07");

    private static StaticDataFileClient ClientOver(HttpMessageHandler network) =>
        new(new HttpClient(network) { BaseAddress = new Uri(BaseAddress) });

    [Fact]
    public async Task AGroupCreateSendsTheKeyHeaderAndLeavesOutAnEmptyPurposeAndExternalRef()
    {
        var network = new RecordingHandler
        {
            ResponseStatusCode = HttpStatusCode.Created,
            ResponseBody = $$"""{"groupId":"{{GroupId}}","owner":"a b&c","name":"Onboarding pack","version":1}"""
        };

        var group = await ClientOver(network).CreateGroupAsync("a b&c", "group-1", "Onboarding pack");

        var sent = network.LastRequest.ShouldNotBeNull();
        sent.Method.ShouldBe(HttpMethod.Post);
        sent.RequestUri.ShouldNotBeNull().AbsoluteUri.ShouldBe($"{BaseAddress}/v1/file-groups?owner=a%20b%26c");
        sent.Headers.GetValues("X-Idempotency-Key").ShouldBe(["group-1"]);
        network.LastBody.ShouldBe("""{"name":"Onboarding pack"}""");
        group.GroupId.ShouldBe(GroupId);
        group.Owner.ShouldBe("a b&c");
        group.Name.ShouldBe("Onboarding pack");
        group.Version.ShouldBe(1);
        group.Purpose.ShouldBeNull();
        group.ExternalRef.ShouldBeNull();
    }

    [Fact]
    public async Task AGroupCreateSendsAGivenPurposeAndExternalRef()
    {
        var network = new RecordingHandler
        {
            ResponseStatusCode = HttpStatusCode.Created,
            ResponseBody = """{"purpose":"customer-onboarding","externalRef":"cust-42"}"""
        };

        var group = await ClientOver(network).CreateGroupAsync(
            "customer-1", "group-1", "Onboarding pack", purpose: "customer-onboarding", externalRef: "cust-42");

        network.LastBody.ShouldBe("""{"name":"Onboarding pack","purpose":"customer-onboarding","externalRef":"cust-42"}""");
        group.Purpose.ShouldBe("customer-onboarding");
        group.ExternalRef.ShouldBe("cust-42");
    }

    [Fact]
    public async Task AGroupUpdateSendsItsVersionAsIfMatchAndReturnsTheNewVersion()
    {
        var network = new RecordingHandler
        {
            ResponseBody = $$"""{"groupId":"{{GroupId}}","name":"Onboarding pack 2","version":2,"updatedBy":"onboarding-svc","updatedOn":"2026-06-10T09:00:00+00:00"}"""
        };

        var group = await ClientOver(network).UpdateGroupAsync("customer-1", GroupId, version: 1, "Onboarding pack 2");

        var sent = network.LastRequest.ShouldNotBeNull();
        sent.Method.ShouldBe(HttpMethod.Put);
        sent.RequestUri.ShouldNotBeNull().AbsoluteUri.ShouldBe($"{BaseAddress}/v1/file-groups/{GroupId}?owner=customer-1");
        sent.Headers.GetValues("If-Match").ShouldBe(["\"1\""]);
        network.LastBody.ShouldBe("""{"name":"Onboarding pack 2"}""");
        group.Name.ShouldBe("Onboarding pack 2");
        group.Version.ShouldBe(2);
        group.UpdatedBy.ShouldBe("onboarding-svc");
        group.UpdatedOn.ShouldBe(new DateTimeOffset(2026, 6, 10, 9, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task AGroupUpdateSendsAGivenPurposeAndExternalRef()
    {
        var network = new RecordingHandler();

        await ClientOver(network).UpdateGroupAsync(
            "customer-1", GroupId, version: 12, "Onboarding pack", purpose: "customer-onboarding", externalRef: "cust-42");

        network.LastRequest.ShouldNotBeNull().Headers.GetValues("If-Match").ShouldBe(["\"12\""]);
        network.LastBody.ShouldBe("""{"name":"Onboarding pack","purpose":"customer-onboarding","externalRef":"cust-42"}""");
    }

    [Fact]
    public async Task AGroupReadAndDeleteCallTheirRoutes()
    {
        var network = new RecordingHandler { ResponseBody = $$"""{"groupId":"{{GroupId}}","name":"Onboarding pack"}""" };
        var client = ClientOver(network);

        var group = await client.GetGroupAsync("customer-1", GroupId);

        network.LastRequest.ShouldNotBeNull().Method.ShouldBe(HttpMethod.Get);
        network.LastRequest.RequestUri.ShouldNotBeNull().AbsoluteUri
            .ShouldBe($"{BaseAddress}/v1/file-groups/{GroupId}?owner=customer-1");
        group.GroupId.ShouldBe(GroupId);
        group.Name.ShouldBe("Onboarding pack");

        await client.DeleteGroupAsync("customer-1", GroupId);

        network.LastRequest.ShouldNotBeNull().Method.ShouldBe(HttpMethod.Delete);
        network.LastRequest.RequestUri.ShouldNotBeNull().AbsoluteUri
            .ShouldBe($"{BaseAddress}/v1/file-groups/{GroupId}?owner=customer-1");
    }

    [Fact]
    public async Task AGroupListSendsThePageDatesPurposeAndExternalRefEncoded()
    {
        var network = new RecordingHandler
        {
            ResponseBody = $$"""{"items":[{"groupId":"{{GroupId}}","name":"Onboarding pack"}],"pageNumber":2,"pageSize":5,"pageCount":2,"totalItemCount":6,"hasNextPage":false,"hasPreviousPage":true}"""
        };

        var page = await ClientOver(network).ListGroupsAsync("customer-1", pageNumber: 2, pageSize: 5,
            fromDate: new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
            toDate: new DateTimeOffset(2026, 6, 30, 23, 59, 59, TimeSpan.FromHours(7)),
            purpose: "customer onboarding", externalRef: "cust&42");

        network.LastRequest.ShouldNotBeNull().Method.ShouldBe(HttpMethod.Get);
        network.LastRequest.RequestUri.ShouldNotBeNull().AbsoluteUri.ShouldBe(
            $"{BaseAddress}/v1/file-groups?owner=customer-1&pageNumber=2&pageSize=5"
            + "&fromDate=2026-06-01T00%3A00%3A00.0000000%2B00%3A00&toDate=2026-06-30T23%3A59%3A59.0000000%2B07%3A00"
            + "&purpose=customer%20onboarding&externalRef=cust%2642");
        page.Items.Select(g => g.GroupId).ShouldBe([GroupId]);
        page.PageNumber.ShouldBe(2);
        page.PageSize.ShouldBe(5);
        page.PageCount.ShouldBe(2);
        page.TotalItemCount.ShouldBe(6);
        page.HasNextPage.ShouldBeFalse();
        page.HasPreviousPage.ShouldBeTrue();
    }

    [Fact]
    public async Task AGroupListWithoutFiltersSendsOnlyTheOwnerAndPage()
    {
        var network = new RecordingHandler();

        await ClientOver(network).ListGroupsAsync("customer-1", pageNumber: 1, pageSize: 10);

        network.LastRequest.ShouldNotBeNull().RequestUri.ShouldNotBeNull().AbsoluteUri
            .ShouldBe($"{BaseAddress}/v1/file-groups?owner=customer-1&pageNumber=1&pageSize=10");
    }

    [Fact]
    public async Task AnUnlinkSendsTheGroupFieldAsNullWithTheVersionAsIfMatch()
    {
        var network = new RecordingHandler { ResponseBody = $$"""{"fileId":"{{FileId}}","version":2}""" };

        var file = await ClientOver(network).LinkAsync("customer-1", FileId, groupId: null, version: 1);

        var sent = network.LastRequest.ShouldNotBeNull();
        sent.Method.ShouldBe(HttpMethod.Put);
        sent.RequestUri.ShouldNotBeNull().AbsoluteUri.ShouldBe($"{BaseAddress}/v1/files/{FileId}/group?owner=customer-1");
        sent.Headers.GetValues("If-Match").ShouldBe(["\"1\""]);
        network.LastBody.ShouldBe("""{"groupId":null}""");
        file.GroupId.ShouldBeNull();
        file.Version.ShouldBe(2);
    }

    [Fact]
    public async Task ALinkSendsTheGroupAndReadsItBack()
    {
        var network = new RecordingHandler { ResponseBody = $$"""{"fileId":"{{FileId}}","groupId":"{{GroupId}}","version":4}""" };

        var file = await ClientOver(network).LinkAsync("customer-1", FileId, GroupId, version: 3);

        network.LastRequest.ShouldNotBeNull().Headers.GetValues("If-Match").ShouldBe(["\"3\""]);
        network.LastBody.ShouldBe($$"""{"groupId":"{{GroupId}}"}""");
        file.GroupId.ShouldBe(GroupId);
        file.Version.ShouldBe(4);
    }

    public static TheoryData<string, HttpStatusCode> Errors()
    {
        var data = new TheoryData<string, HttpStatusCode>();
        foreach (var call in Calls.Keys)
        {
            foreach (var status in (HttpStatusCode[])
                     [HttpStatusCode.Conflict, HttpStatusCode.PreconditionFailed, HttpStatusCode.NotFound, HttpStatusCode.ServiceUnavailable])
            {
                data.Add(call, status);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Errors))]
    public async Task EveryGroupAndLinkErrorBecomesATypedErrorWithItsStatus(string call, HttpStatusCode status)
    {
        var network = new RecordingHandler { ResponseStatusCode = status, ResponseBody = $$"""{"status":{{(int)status}}}""" };

        var error = await Should.ThrowAsync<StaticDataApiException>(() => Calls[call](ClientOver(network)));

        error.StatusCode.ShouldBe(status);
        error.ProblemDetails.ShouldNotBeNull().Status.ShouldBe((int)status);
    }

    private static readonly Dictionary<string, Func<StaticDataFileClient, Task>> Calls = new()
    {
        ["create"] = c => c.CreateGroupAsync("customer-1", "group-1", "Onboarding pack"),
        ["list"] = c => c.ListGroupsAsync("customer-1", pageNumber: 1, pageSize: 10),
        ["read"] = c => c.GetGroupAsync("customer-1", GroupId),
        ["update"] = c => c.UpdateGroupAsync("customer-1", GroupId, version: 1, "Onboarding pack 2"),
        ["delete"] = c => c.DeleteGroupAsync("customer-1", GroupId),
        ["link"] = c => c.LinkAsync("customer-1", FileId, GroupId, version: 1)
    };
}
