using DKNet.StaticData.App.Tests.Files.Support;
using DKNet.StaticData.App.TestSupport;

namespace DKNet.StaticData.App.Tests.Files;

/// <summary>
/// Spec DRK-2206 §5 "Callers and roles". A caller proves who it is with a token for this API carrying a caller id
/// (<c>client_id</c>, <c>azp</c> or <c>appid</c>) and holds app roles in the token's <c>roles</c> claim; every
/// <c>GET</c> needs <c>staticdata.read</c>, every <c>POST</c> and <c>DELETE</c> needs <c>staticdata.write</c>.
/// </summary>
public sealed class CallersAndRolesTests(FilesHost hosts) : IClassFixture<FilesHost>
{
    public static IEnumerable<object[]> RefusedCallers => Databases.With(
        ["report-svc", new[] { Callers.Read }, "upload \"visa.pdf\""],
        ["report-svc", new[] { Callers.Read }, "delete \"passport.pdf\""],
        ["ingest-svc", new[] { Callers.Write }, "list the files"],
        ["ingest-svc", new[] { Callers.Write }, "read the details of \"passport.pdf\""],
        ["ingest-svc", new[] { Callers.Write }, "download \"passport.pdf\""],
        ["scope-svc", Array.Empty<string>(), "list the files"]);

    /// <summary>Scenario Outline: A route refuses a caller without the role it needs.</summary>
    [Theory]
    [MemberData(nameof(RefusedCallers))]
    public async Task ARouteRefusesACallerWithoutTheRoleItNeeds(
        TestDatabase database, string caller, string[] roles, string action)
    {
        var api = await hosts.OnAsync(database);
        var fileId = await api.HasFileAsync("customer-1", "passport.pdf");
        var token = roles.Length > 0
            ? Callers.Holding(caller, roles)
            // "no role, only the scope staticdata.read"
            : TestTokens.Mint(new Dictionary<string, object> { ["client_id"] = caller, ["scp"] = Callers.Read });

        using var response = action switch
        {
            "upload \"visa.pdf\"" => await api.UploadAsync(token, "customer-1", "visa.pdf", Bytes.Of(1024), FilesApi.NewKey()),
            "delete \"passport.pdf\"" => await api.DeleteAsync(token, "customer-1", fileId),
            "list the files" => await api.ListAsync(token, "customer-1"),
            "read the details of \"passport.pdf\"" => await api.ReadAsync(token, "customer-1", fileId),
            "download \"passport.pdf\"" => await api.DownloadAsync(token, "customer-1", fileId),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        };

        await response.ShouldBeProblemDetailsAsync(HttpStatusCode.Forbidden);
        (await api.FileNamesAsync("customer-1")).ShouldBe(["passport.pdf"]);
    }

    public static IEnumerable<object[]> InvalidTokens => Databases.With(
        ["no token"],
        ["an expired token"],
        ["a token with only \"oid\" and \"sub\" claims"]);

    /// <summary>Scenario Outline: A call without a valid token with a caller id is refused.</summary>
    [Theory]
    [MemberData(nameof(InvalidTokens))]
    public async Task ACallWithoutAValidTokenWithACallerIdIsRefused(TestDatabase database, string token)
    {
        var api = await hosts.OnAsync(database);
        var bearer = token switch
        {
            "no token" => null,
            "an expired token" => TestTokens.Mint(
                new Dictionary<string, object> { ["client_id"] = "onboarding-svc", ["roles"] = new[] { Callers.Read } },
                // an hour past expiry, well beyond the bearer scheme's default 5-minute clock skew
                expires: DateTime.UtcNow.AddHours(-1)),
            "a token with only \"oid\" and \"sub\" claims" => TestTokens.Mint(
                new Dictionary<string, object> { ["oid"] = "user-55", ["sub"] = "user-55" }),
            _ => throw new ArgumentOutOfRangeException(nameof(token), token, null)
        };

        using var response = await api.ListAsync(bearer, "customer-1");

        await response.ShouldHaveStatusAsync(HttpStatusCode.Unauthorized);
    }

    /// <summary>Scenario: The role check comes before every input check.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task TheRoleCheckComesBeforeEveryInputCheck(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);

        using var response = await api.UploadAsync(Callers.ReportSvc, owner: null, "passport.pdf", Bytes.Of(1024), key: null);

        await response.ShouldBeProblemDetailsAsync(HttpStatusCode.Forbidden);
    }

    /// <summary>Scenario: An upload refused for its role does not use up its idempotency key.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task AnUploadRefusedForItsRoleDoesNotUseUpItsIdempotencyKey(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        using (var refused = await api.UploadAsync(
                   Callers.Holding("ingest-svc"), "customer-1", "passport.pdf", Bytes.Of(1024), "upload-7"))
        {
            await refused.ShouldHaveStatusAsync(HttpStatusCode.Forbidden);
        }

        using var response = await api.UploadAsync(
            Callers.Holding("ingest-svc", Callers.Write), "customer-1", "passport.pdf", Bytes.Of(1024), "upload-7");

        await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
    }

    /// <summary>Scenario: The audit stamp is the caller id, never the owner.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task TheAuditStampIsTheCallerIdNeverTheOwner(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var token = TestTokens.Mint(new Dictionary<string, object>
        {
            ["appid"] = "onboarding-svc",
            ["oid"] = "user-55",
            ["roles"] = new[] { Callers.Read, Callers.Write }
        });

        using var response = await api.UploadAsync(token, "customer-1", "passport.pdf", Bytes.Of(1024), FilesApi.NewKey());

        await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
        var details = await response.JsonAsync();
        details.GetProperty("createdBy").GetString().ShouldBe("onboarding-svc");
        details.GetProperty("owner").GetString().ShouldBe("customer-1");
    }

    public static IEnumerable<object[]> HealthRoutes => Databases.With(
        ["health status route"],
        ["health detail route"]);

    /// <summary>Scenario Outline: The health routes stay anonymous.</summary>
    [Theory]
    [MemberData(nameof(HealthRoutes))]
    public async Task TheHealthRoutesStayAnonymous(TestDatabase database, string route)
    {
        var api = await hosts.OnAsync(database);

        using var response = await api.SendAsync(
            HttpMethod.Get, route == "health status route" ? "/healthz" : "/healthz/detail", token: null);

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        if (route == "health status route")
        {
            (await response.Content.ReadAsStringAsync()).ShouldBe("""{"status":"Healthy"}""");
        }
        else
        {
            (await response.JsonAsync()).GetProperty("entries").GetProperty("CoreDbContext").GetProperty("status")
                .GetString().ShouldBe("Healthy");
        }
    }
}
