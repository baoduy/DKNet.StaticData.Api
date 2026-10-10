using System.Security.Claims;
using DKNet.StaticData.Api.Configs.Handlers;
using Microsoft.AspNetCore.Http;

namespace DKNet.StaticData.App.Tests.Unit.Configs;

/// <summary>ADR-0012: an idempotency key is scoped by caller id and owner, and no 2 different pairs share a scope.</summary>
public class IdempotencyKeyScopeTests
{
    [Fact]
    public void TheSameCallerAndOwnerGiveTheSameScope()
    {
        var scope = IdempotencyKeyScope.Resolve(Call("onboarding-svc", "customer-1"));

        scope.ShouldBe("14:onboarding-svc:customer-1");
        IdempotencyKeyScope.Resolve(Call("onboarding-svc", "customer-1")).ShouldBe(scope);
    }

    [Fact]
    public void ACallerAndOwnerSplitAtAnotherColonGiveAnotherScope()
    {
        var first = IdempotencyKeyScope.Resolve(Call("a", "b:c"));
        var second = IdempotencyKeyScope.Resolve(Call("a:b", "c"));

        first.ShouldBe("1:a:b:c");
        second.ShouldBe("3:a:b:c");
    }

    private static HttpContext Call(string callerId, string owner) =>
        new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("client_id", callerId)], "Bearer")),
            Request = { QueryString = QueryString.Create("owner", owner) }
        };
}
