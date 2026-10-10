using System.Security.Claims;
using DKNet.StaticData.Api.Configs.Handlers;

namespace DKNet.StaticData.App.Tests.Unit.Configs;

/// <summary>
/// Brief DRK-2209 §6a D1: the caller id is the first non-blank of <c>client_id</c>, <c>azp</c> and <c>appid</c>; a
/// token with none of them (only <c>oid</c>/<c>sub</c>) has no caller id.
/// </summary>
public class CallerAccessorTests
{
    public static TheoryData<string[], string?> Claims => new()
    {
        { ["client_id=onboarding-svc", "azp=web-app", "appid=legacy-app"], "onboarding-svc" },
        { ["azp=web-app"], "web-app" },
        { ["appid=legacy-app"], "legacy-app" },
        { ["client_id= ", "azp=web-app"], "web-app" },
        { ["oid=user-55", "sub=user-55"], null }
    };

    [Theory]
    [MemberData(nameof(Claims))]
    public void TheCallerIdIsTheFirstNonBlankCallerClaim(string[] claims, string? expected)
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            claims.Select(c => new Claim(c[..c.IndexOf('=', StringComparison.Ordinal)], c[(c.IndexOf('=', StringComparison.Ordinal) + 1)..])),
            "Bearer"));

        CallerAccessor.Read(user).ShouldBe(expected);
    }

    [Fact]
    public void ACallWithNoUserHasNoCallerId() => CallerAccessor.Read(null).ShouldBeNull();
}
