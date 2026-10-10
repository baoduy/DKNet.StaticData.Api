using System.Text.RegularExpressions;
using DKNet.StaticData.App.Tests.Files.Support;
using DKNet.StaticData.App.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;

namespace DKNet.StaticData.App.Tests.Unit.Files;

/// <summary>
/// Spec DRK-2206 §5 "Limits and settings": each file route's request body limit and timeout, as the started service
/// applies them — the route's own limit when it sets one, else the service-wide Kestrel body limit and default request
/// timeout. Each route must exist first, so a route that is missing never passes on the service-wide defaults.
/// </summary>
public sealed class RouteLimitsTests(FilesHost hosts) : IClassFixture<FilesHost>
{
    public static TheoryData<string, long, int> Routes => new()
    {
        { "upload route", 51_000_000, 300 },
        { "download route", 1_048_576, 300 },
        { "list route", 1_048_576, 30 },
        { "delete route", 1_048_576, 30 }
    };

    /// <summary>Scenario Outline: Each file route has its own request limits.</summary>
    [Theory]
    [MemberData(nameof(Routes))]
    public async Task EachFileRouteHasItsOwnRequestLimits(string route, long body, int seconds)
    {
        var api = await hosts.OnAsync(TestDatabase.Postgres); // the service started with its default settings
        var services = api.Host.Services;
        var (method, pattern) = route switch
        {
            "upload route" => ("POST", @"files/?$"),
            "download route" => ("GET", @"files/\{fileId[^}]*\}/content/?$"),
            "list route" => ("GET", @"files/?$"),
            "delete route" => ("DELETE", @"files/\{fileId[^}]*\}/?$"),
            _ => throw new ArgumentOutOfRangeException(nameof(route), route, null)
        };

        var endpoint = services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => Regex.IsMatch(e.RoutePattern.RawText ?? string.Empty, pattern, RegexOptions.None, TimeSpan.FromSeconds(1)))
            .Where(e => e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Contains(method) == true)
            .ShouldHaveSingleItem($"the service serves no {route}");

        var bodyLimit = endpoint.Metadata.GetMetadata<IRequestSizeLimitMetadata>()?.MaxRequestBodySize
                        ?? services.GetRequiredService<IOptions<KestrelServerOptions>>().Value.Limits.MaxRequestBodySize;
        bodyLimit.ShouldBe(body);
        Timeout(endpoint, services.GetRequiredService<IOptions<RequestTimeoutOptions>>().Value)
            .ShouldBe(TimeSpan.FromSeconds(seconds));
    }

    /// <summary>The timeout the request-timeout middleware applies to <paramref name="endpoint"/>.</summary>
    private static TimeSpan? Timeout(Endpoint endpoint, RequestTimeoutOptions options)
    {
        if (endpoint.Metadata.GetMetadata<DisableRequestTimeoutAttribute>() is not null)
        {
            return null;
        }

        if (endpoint.Metadata.GetMetadata<RequestTimeoutPolicy>() is { } policy)
        {
            return policy.Timeout;
        }

        if (endpoint.Metadata.GetMetadata<RequestTimeoutAttribute>() is { } attribute)
        {
            return attribute.PolicyName is { } name ? options.Policies[name].Timeout : attribute.Timeout;
        }

        return options.DefaultPolicy?.Timeout;
    }
}
