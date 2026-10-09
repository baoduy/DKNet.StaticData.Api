using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using DKNet.StaticData.App.TestSupport;
using DKNet.StaticData.App.Tests.Integration.Support;
using DKNet.StaticData.AppServices.ManualSample.V1;
using DKNet.StaticData.Domains.Features.ManualSample.Entities;
using DKNet.StaticData.Infra.Contexts;
using DKNet.StaticData.Share;

namespace DKNet.StaticData.App.Tests.Integration.ManualSample.V1;

/// <summary>
/// DRK-1901 acceptance tests: the hand-written <c>PurchaseOrder</c> sample under the same row-level ownership
/// boundary <c>ProductOwnershipIsolationTests</c> proves for <c>Product</c>. Two authenticated callers share one
/// host and database via <see cref="MultiSubjectAuthHandler"/>; caller B must get 404 on every route that takes
/// caller A's order id, and must never see it in their list. Every write here is driven over HTTP only, so the
/// tests depend on no production symbol the fix introduces.
/// </summary>
/// <remarks>
/// <see cref="MultiSubjectAuthHandler"/> always sends the same <c>Name</c> claim, so <c>CreatedBy</c> (bound from
/// <c>[FromClaim(ClaimTypes.Name)]</c>) is identical for both callers. Only the subject header differs — the
/// isolation asserted here must key off the ownership key, never off <c>CreatedBy</c>.
/// </remarks>
public sealed class PurchaseOrderOwnershipIsolationTests(AuthOnMultiSubjectApiFixture fixture)
    : IClassFixture<AuthOnMultiSubjectApiFixture>
{
    private const string CallerA = "opaque-subject-a";
    private const string CallerB = "opaque-subject-b";

    [Fact]
    public async Task DifferentSubjects_CallerBGetsNotFound_WhenReadingCallerAsOrder()
    {
        await fixture.ResetDatabaseAsync();
        var client = fixture.CreateClient();
        var orderA = await CreateOrderAsync(client, CallerA, "Acme Pte Ltd", 100.00m);

        using var crossRead = await SendAsAsync(client, HttpMethod.Get, $"/v1/purchase-orders/{orderA.Id}", CallerB);
        crossRead.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // A real per-caller filter, not "everyone denied": the owner still reads it back.
        var ownRead = await GetAsAsync(client, orderA.Id, CallerA);
        ownRead.Id.ShouldBe(orderA.Id);
        ownRead.CustomerName.ShouldBe("Acme Pte Ltd");
    }

    [Fact]
    public async Task DifferentSubjects_CallerAsOrderIsAbsentFromCallerBsList()
    {
        await fixture.ResetDatabaseAsync();
        var client = fixture.CreateClient();
        var orderA = await CreateOrderAsync(client, CallerA, "Acme Pte Ltd", 100.00m);
        var orderB = await CreateOrderAsync(client, CallerB, "Globex Corporation", 200.00m);

        var listB = await ListAsAsync(client, CallerB);
        listB.ShouldContain(o => o.Id == orderB.Id);
        listB.ShouldNotContain(o => o.Id == orderA.Id);

        var listA = await ListAsAsync(client, CallerA);
        listA.ShouldContain(o => o.Id == orderA.Id);
        listA.ShouldNotContain(o => o.Id == orderB.Id);
    }

    [Fact]
    public async Task DifferentSubjects_CallerBGetsNotFound_WhenUpdatingCallerAsOrder_AndAmountIsUnchanged()
    {
        await fixture.ResetDatabaseAsync();
        var client = fixture.CreateClient();
        var orderA = await CreateOrderAsync(client, CallerA, "Acme Pte Ltd", 100.00m);

        using var crossUpdate = await SendAsAsync(client, HttpMethod.Put, $"/v1/purchase-orders/{orderA.Id}", CallerB,
            JsonContent.Create(new { amount = 999.00m }));
        crossUpdate.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var ownRead = await GetAsAsync(client, orderA.Id, CallerA);
        ownRead.Amount.ShouldBe(100.00m);
    }

    [Fact]
    public async Task DifferentSubjects_CallerBGetsNotFound_WhenCancellingCallerAsOrder_AndStatusIsUnchanged()
    {
        await fixture.ResetDatabaseAsync();
        var client = fixture.CreateClient();
        var orderA = await CreateOrderAsync(client, CallerA, "Acme Pte Ltd", 100.00m);

        using var crossCancel = await SendAsAsync(client, HttpMethod.Post, $"/v1/purchase-orders/{orderA.Id}/cancel", CallerB);
        crossCancel.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var ownRead = await GetAsAsync(client, orderA.Id, CallerA);
        ownRead.Status.ShouldBe(PurchaseOrderStatus.Placed);
    }

    [Fact]
    public async Task DifferentSubjects_CallerBGetsNotFound_WhenDeletingCallerAsOrder_AndOrderStillExists()
    {
        await fixture.ResetDatabaseAsync();
        var client = fixture.CreateClient();
        var orderA = await CreateOrderAsync(client, CallerA, "Acme Pte Ltd", 100.00m);

        using var crossDelete = await SendAsAsync(client, HttpMethod.Delete, $"/v1/purchase-orders/{orderA.Id}", CallerB);
        crossDelete.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var ownRead = await GetAsAsync(client, orderA.Id, CallerA);
        ownRead.Id.ShouldBe(orderA.Id);
    }

    [Fact]
    public async Task AuthenticatedCallerWithNoSubjectClaim_CreateIsRefusedWithForbidden_NoRowPersisted()
    {
        await fixture.ResetDatabaseAsync();
        var client = fixture.CreateClient();

        // Deliberately no X-Test-Subject / X-Test-Oid header: authenticated, Name claim present (so ByUser and
        // CreatedBy resolve), but no ownership key.
        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/purchase-orders")
        {
            Content = JsonContent.Create(new { customerName = "Orphan Traders", amount = 1.23m })
        };
        createRequest.Headers.Add("X-Idempotency-Key", Guid.NewGuid().ToString());
        using var createResponse = await client.SendAsync(createRequest);

        createResponse.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        var persisted = await dbContext.Set<PurchaseOrder>().IgnoreQueryFilters()
            .AnyAsync(p => p.CustomerName == "Orphan Traders");
        persisted.ShouldBeFalse("a refused create must not leave a row behind, readable or not");
    }

    private static async Task<PurchaseOrderDto> CreateOrderAsync(
        HttpClient client, string subject, string customerName, decimal amount)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/purchase-orders")
        {
            Content = JsonContent.Create(new { customerName, amount })
        };
        request.Headers.Add(MultiSubjectAuthHandler.SubjectHeaderName, subject);
        request.Headers.Add("X-Idempotency-Key", Guid.NewGuid().ToString());

        using var response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<PurchaseOrderDto>(SharedConsts.JsonSerializerOptions))!;
    }

    private static async Task<PurchaseOrderDto> GetAsAsync(HttpClient client, Guid id, string subject)
    {
        using var response = await SendAsAsync(client, HttpMethod.Get, $"/v1/purchase-orders/{id}", subject);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<PurchaseOrderDto>(SharedConsts.JsonSerializerOptions))!;
    }

    private static async Task<List<PurchaseOrderDto>> ListAsAsync(HttpClient client, string subject)
    {
        using var response = await SendAsAsync(client, HttpMethod.Get, "/v1/purchase-orders?pageIndex=1&pageSize=100", subject);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<List<PurchaseOrderDto>>(SharedConsts.JsonSerializerOptions))!;
    }

    private static async Task<HttpResponseMessage> SendAsAsync(
        HttpClient client, HttpMethod method, string url, string subject, HttpContent? content = null)
    {
        using var request = new HttpRequestMessage(method, url) { Content = content };
        request.Headers.Add(MultiSubjectAuthHandler.SubjectHeaderName, subject);
        return await client.SendAsync(request);
    }
}
