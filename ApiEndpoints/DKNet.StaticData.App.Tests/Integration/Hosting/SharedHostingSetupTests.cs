using System.Net.Http.Headers;
using System.Text;
using DKNet.StaticData.App.Tests.Files.Support;
using Microsoft.AspNetCore.Http;

namespace DKNet.StaticData.App.Tests.Integration.Hosting;

/// <summary>
/// Spec DRK-2366 §5 (Feature: Shared API hosting setup in DKNet packages), the DKNet.StaticData.Api rows, and its
/// §3 "Must stay true" items: the service answers as before once it takes its hosting setup from the DKNet packages.
/// Each scenario starts the service with its shipped settings files and turns items on or off through host settings
/// only (<see cref="HostingHost"/>).
/// </summary>
public sealed class SharedHostingSetupTests
{
    private const string Route = "/healthz";
    private const string AllowedOrigin = "https://app.example.com";

    /// <summary>Spec §3 Must stay true: "Rate limiting never lets a caller pick its own bucket with a value it sends:
    /// … the host name" — Scenario: Requests with no client address share one bucket.</summary>
    [Fact]
    public async Task RequestsWithNoClientAddress_ShareOneBucket()
    {
        await using var host = await HostingHost.StartAsync(new Dictionary<string, string?>
        {
            ["FeatureManagement__EnableRateLimit"] = "true",
            ["RateLimit__DefaultRequestLimit"] = "2",
            ["RateLimit__TimeWindowInSeconds"] = "1"
        });
        await WarmUpAsync(host);

        var first = await host.SendAsync("GET", Route, host: "a.example");
        var second = await host.SendAsync("GET", Route, host: "b.example");
        var third = await host.SendAsync("GET", Route, host: "c.example");

        first.Response.StatusCode.ShouldNotBe(StatusCodes.Status429TooManyRequests);
        second.Response.StatusCode.ShouldNotBe(StatusCodes.Status429TooManyRequests);
        third.Response.StatusCode.ShouldBe(StatusCodes.Status429TooManyRequests);
    }

    /// <summary>Scenario Outline: Each service trusts forwarded headers from its trusted network — DKNet.StaticData.Api.</summary>
    [Theory]
    [InlineData("10.1.2.3", "198.51.100.7")]
    [InlineData("203.0.113.9", "203.0.113.9")]
    public async Task TrustsForwardedHeadersFromItsTrustedNetwork(string peer, string client)
    {
        await using var host = await HostingHost.StartAsync(new Dictionary<string, string?>
        {
            ["Security__TrustedNetworks__0"] = "10.0.0.0/8"
        });

        var request = await host.SendAsync("GET", Route, peer, headers: ForwardedFor("198.51.100.7"));

        request.Connection.RemoteIpAddress.ShouldBe(IPAddress.Parse(client));
    }

    /// <summary>Scenario Outline: Each service keeps its shipped rate limit — DKNet.StaticData.Api. The caller is one
    /// client address: the service limits requests before it signs the caller in, so a user name never picks the bucket.</summary>
    [Fact]
    public async Task KeepsItsShippedRateLimit()
    {
        await using var host = await HostingHost.StartAsync(new Dictionary<string, string?>
        {
            ["FeatureManagement__EnableRateLimit"] = "true"
        });
        await WarmUpAsync(host);

        var statuses = await SendManyAsync(host, 101, "203.0.113.9");

        statuses.Take(100).ShouldNotContain(StatusCodes.Status429TooManyRequests);
        statuses[^1].ShouldBe(StatusCodes.Status429TooManyRequests);
    }

    /// <summary>Scenario Outline: Each service keeps the CORS headers its settings allow — DKNet.StaticData.Api, X-Idempotency-Key.</summary>
    [Fact]
    public async Task KeepsTheCorsHeadersItsSettingsAllow()
    {
        await using var host = await HostingHost.StartAsync(new Dictionary<string, string?>
        {
            ["Cors__AllowedOrigins__0"] = AllowedOrigin
        });

        var preflight = await SendPreflightAsync(host, "POST", "X-Idempotency-Key");

        preflight.Response.StatusCode.ShouldBe(StatusCodes.Status204NoContent);
        preflight.Response.Headers.AccessControlAllowOrigin.ToString().ShouldBe(AllowedOrigin);
        preflight.Response.Headers.AccessControlAllowHeaders.ToString().Split(',')
            .ShouldBe(["Authorization", "Content-Type", "Accept", "X-Idempotency-Key"]);
    }

    /// <summary>Spec §3 Must stay true: CORS stays off when no allowed origin is configured (shipped
    /// <c>Cors:AllowedOrigins</c> is empty).</summary>
    [Fact]
    public async Task CorsStaysOff_WhenNoAllowedOriginIsConfigured()
    {
        await using var host = await HostingHost.StartAsync(new Dictionary<string, string?>());

        var preflight = await SendPreflightAsync(host, "POST", "X-Idempotency-Key");

        preflight.Response.Headers.AccessControlAllowOrigin.ShouldBeEmpty();
        preflight.Response.Headers.AccessControlAllowHeaders.ShouldBeEmpty();
        preflight.Response.Headers.AccessControlAllowMethods.ShouldBeEmpty();
    }

    /// <summary>Scenario Outline: An item switched off by its feature flag changes nothing — DKNet.StaticData.Api, HTTPS.</summary>
    [Fact]
    public async Task HttpsSwitchedOff_PlainHttpIsNotRedirected_AndGetsNoHsts()
    {
        await using var host = await HostingHost.StartAsync(HttpsSettings(enabled: false));

        var plain = await host.SendAsync("GET", Route, host: "api.example.com");
        var secure = await host.SendAsync("GET", Route, host: "api.example.com", scheme: "https");

        plain.Response.StatusCode.ShouldNotBeOneOf(StatusCodes.Status307TemporaryRedirect, StatusCodes.Status308PermanentRedirect);
        plain.Response.Headers.Location.ShouldBeEmpty();
        plain.Response.Headers.StrictTransportSecurity.ShouldBeEmpty();
        secure.Response.Headers.StrictTransportSecurity.ShouldBeEmpty();
    }

    /// <summary>Presence check for the HTTPS row above: the same requests with the flag on are redirected, and HTTPS
    /// gets HSTS with the shipped <c>Https:HstsMaxAgeDays</c> (365 days).</summary>
    [Fact]
    public async Task HttpsSwitchedOn_PlainHttpIsRedirected_AndHttpsGetsHsts()
    {
        await using var host = await HostingHost.StartAsync(HttpsSettings(enabled: true));

        var plain = await host.SendAsync("GET", Route, host: "api.example.com");
        var secure = await host.SendAsync("GET", Route, host: "api.example.com", scheme: "https");

        plain.Response.StatusCode.ShouldBe(StatusCodes.Status307TemporaryRedirect);
        plain.Response.Headers.Location.ToString().ShouldBe("https://api.example.com/healthz");
        secure.Response.Headers.StrictTransportSecurity.ToString()
            .ShouldBe("max-age=31536000; includeSubDomains; preload");
    }

    /// <summary>Scenario Outline: An item switched off by its feature flag changes nothing — DKNet.StaticData.Api, rate limiting.</summary>
    [Fact]
    public async Task RateLimitingSwitchedOff_101RequestsWithinOneSecond_NoneRefused()
    {
        await using var host = await HostingHost.StartAsync(new Dictionary<string, string?>
        {
            ["FeatureManagement__EnableRateLimit"] = "false"
        });
        await WarmUpAsync(host);

        var statuses = await SendManyAsync(host, 101, "203.0.113.9");

        statuses.ShouldNotContain(StatusCodes.Status429TooManyRequests);
    }

    /// <summary>Scenario Outline: An item switched off by its feature flag changes nothing — DKNet.StaticData.Api, forwarded headers.</summary>
    [Fact]
    public async Task ForwardedHeadersSwitchedOff_ServiceSeesThePeer()
    {
        await using var host = await HostingHost.StartAsync(new Dictionary<string, string?>
        {
            ["FeatureManagement__EnableForwardedHeaders"] = "false",
            ["Security__TrustedNetworks__0"] = "10.0.0.0/8"
        });

        var request = await host.SendAsync("GET", Route, "10.1.2.3", headers: ForwardedFor("198.51.100.7"));

        request.Connection.RemoteIpAddress.ShouldBe(IPAddress.Parse("10.1.2.3"));
    }

    /// <summary>Scenario Outline: An item switched off by its feature flag changes nothing — DKNet.StaticData.Api,
    /// request bounds. The body is read in full and reaches validation, which refuses it as an empty setting.</summary>
    [Fact]
    public async Task RequestBoundsSwitchedOff_TwoMiBBody_NotRefusedForItsSize()
    {
        using var response = await PostTwoMiBAsync(enabled: false);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    /// <summary>Presence check for the request-bounds row above: the same body with the flag on is refused by the
    /// shipped <c>RequestBounds:MaxRequestBodySizeBytes</c> (1 MiB).</summary>
    [Fact]
    public async Task RequestBoundsSwitchedOn_TwoMiBBody_RefusedWith413()
    {
        using var response = await PostTwoMiBAsync(enabled: true);

        response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
    }

    /// <summary>Scenario Outline: Antiforgery stays off in each service — DKNet.StaticData.Api. No cookie at all: neither
    /// <c>XSRF-TOKEN</c> nor any other antiforgery cookie.</summary>
    [Fact]
    public async Task AntiforgeryStaysOff_GetSetsNoXsrfTokenCookie()
    {
        await using var host = await HostingHost.StartAsync(new Dictionary<string, string?>());

        var request = await host.SendAsync("GET", Route, scheme: "https");

        request.Response.Headers.SetCookie.ShouldBeEmpty();
    }

    /// <summary>Presence check for the antiforgery row above: with the flag on, the same GET sets a cookie.</summary>
    [Fact]
    public async Task AntiforgerySwitchedOn_GetSetsACookie()
    {
        await using var host = await HostingHost.StartAsync(new Dictionary<string, string?>
        {
            ["FeatureManagement__EnableAntiforgery"] = "true"
        });

        var request = await host.SendAsync("GET", Route, scheme: "https");

        request.Response.Headers.SetCookie.ShouldNotBeEmpty();
    }

    /// <summary>One request from a client address no scenario uses, so the host's first-request cost is not spent
    /// inside a scenario's one-second window.</summary>
    private static Task WarmUpAsync(HostingHost host) => host.SendAsync("GET", Route, "192.0.2.1");

    private static async Task<int[]> SendManyAsync(HostingHost host, int count, string peer)
    {
        var statuses = new int[count];
        for (var i = 0; i < count; i++)
        {
            statuses[i] = (await host.SendAsync("GET", Route, peer)).Response.StatusCode;
        }

        return statuses;
    }

    private static Dictionary<string, string> ForwardedFor(string client) => new() { ["X-Forwarded-For"] = client };

    private static Task<HttpContext> SendPreflightAsync(HostingHost host, string method, string header) =>
        host.SendAsync("OPTIONS", "/v1/ui-settings", headers: new Dictionary<string, string>
        {
            ["Origin"] = AllowedOrigin,
            ["Access-Control-Request-Method"] = method,
            ["Access-Control-Request-Headers"] = header
        });

    private static Dictionary<string, string?> HttpsSettings(bool enabled) => new()
    {
        ["FeatureManagement__EnableHttps"] = enabled ? "true" : "false",
        ["HTTPS_PORT"] = "443"
    };

    /// <summary>Posts a 2 MiB setting body (white space around <c>{}</c>) as a signed-in caller, on real Kestrel:
    /// TestServer applies no Kestrel body limit.</summary>
    private static async Task<HttpResponseMessage> PostTwoMiBAsync(bool enabled)
    {
        await using var host = await HostingHost.StartAsync(new Dictionary<string, string?>
        {
            ["FeatureManagement__EnableRequestBounds"] = enabled ? "true" : "false",
            ["FeatureManagement__RequireAuthorization"] = "true",
            ["FeatureManagement__EnableDemoAuthentication"] = "false"
        }, kestrel: true);
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.ExpectContinue = true;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Callers.LayoutSvc);
        var body = new ByteArrayContent(Encoding.UTF8.GetBytes(new string(' ', (2 * 1_048_576) - 2) + "{}"));
        body.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        return await client.PostAsync("/v1/ui-settings?owner=backoffice-web", body);
    }
}
