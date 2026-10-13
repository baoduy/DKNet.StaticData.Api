using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using DKNet.StaticData.Api.Configs.Handlers;
using DKNet.StaticData.AppServices.Share;
using DKNet.StaticData.Share;
using Moq;

namespace DKNet.StaticData.App.Tests.Unit.Configs;

/// <summary>
/// DRK-1574: <see cref="IPrincipalProvider.Email"/>/<see cref="IPrincipalProvider.UserName"/> are declared
/// non-nullable, so <see cref="PrincipalProvider"/> must never hand back <c>null</c> on any path — a missing
/// <see cref="HttpContext"/>, an anonymous caller, or an authenticated caller with no matching claim.
/// </summary>
public class PrincipalProviderTests
{
    #region Methods

    private static PrincipalProvider CreateProvider(HttpContext? context)
    {
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.SetupGet(a => a.HttpContext).Returns(context);
        return new PrincipalProvider(accessor.Object);
    }

    private static HttpContext CreateAuthenticatedContext(IEnumerable<Claim> claims)
    {
        var identity = new ClaimsIdentity(claims, "TestScheme");
        return new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
    }

    private static HttpContext CreateAnonymousContext()
    {
        var identity = new ClaimsIdentity();
        return new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
    }

    [Fact]
    public void Email_NullHttpContext_IsEmpty()
    {
        var provider = CreateProvider(null);

        provider.Email.ShouldBe(string.Empty);
    }

    [Fact]
    public void UserName_NullHttpContext_IsEmpty()
    {
        var provider = CreateProvider(null);

        provider.UserName.ShouldBe(string.Empty);
    }

    [Fact]
    public void Email_AnonymousCaller_IsEmpty()
    {
        var provider = CreateProvider(CreateAnonymousContext());

        provider.Email.ShouldBe(string.Empty);
    }

    [Fact]
    public void UserName_AnonymousCaller_IsEmpty()
    {
        var provider = CreateProvider(CreateAnonymousContext());

        provider.UserName.ShouldBe(string.Empty);
    }

    [Fact]
    public void OwnershipKey_AnonymousCaller_IsSystemAccount()
    {
        var provider = CreateProvider(CreateAnonymousContext());

        provider.GetOwnershipKey().ShouldBe(SharedConsts.SystemAccount);
    }

    [Fact]
    public void CurrentUser_AnonymousCaller_IsSystemAccount()
    {
        var provider = CreateProvider(CreateAnonymousContext());

        provider.GetCurrentUser().ShouldBe(SharedConsts.SystemAccount);
    }

    [Fact]
    public void Email_AuthenticatedWithoutEmailClaim_IsEmpty()
    {
        var context = CreateAuthenticatedContext([new Claim(ClaimTypes.NameIdentifier, "client-credentials-sub")]);
        var provider = CreateProvider(context);

        provider.Email.ShouldBe(string.Empty);
    }

    [Fact]
    public void UserName_AuthenticatedWithoutNameOrEmailClaim_IsEmpty()
    {
        var context = CreateAuthenticatedContext([new Claim(ClaimTypes.NameIdentifier, "client-credentials-sub")]);
        var provider = CreateProvider(context);

        provider.UserName.ShouldBe(string.Empty);
    }

    [Fact]
    public void UserName_AuthenticatedWithEmailClaimButNoNameClaim_FallsBackToEmail()
    {
        var context = CreateAuthenticatedContext([new Claim("email", "user@example.com")]);
        var provider = CreateProvider(context);

        provider.UserName.ShouldBe("user@example.com");
    }

    [Theory]
    [InlineData("email")]
    [InlineData("emails")]
    public void EmailAndUserName_AuthenticatedWithBothClaims_ReturnClaimValues(string emailClaimType)
    {
        var context = CreateAuthenticatedContext(
        [
            new Claim(ClaimTypes.Name, "Jane Doe"),
            new Claim(emailClaimType, "jane.doe@example.com")
        ]);
        var provider = CreateProvider(context);

        provider.UserName.ShouldBe("Jane Doe");
        provider.Email.ShouldBe("jane.doe@example.com");
    }

    [Fact]
    public void OwnershipKeyAndCurrentUser_AuthenticatedCaller_AreTheOwnerQueryAndTheCallerId()
    {
        var context = CreateAuthenticatedContext([new Claim("client_id", "onboarding-svc"), new Claim("oid", "user-55")]);
        context.Request.QueryString = new QueryString("?owner=Customer-1");
        var provider = CreateProvider(context);

        provider.GetCurrentUser().ShouldBe("onboarding-svc");
        provider.GetOwnershipKey().ShouldBe("Customer-1");
        provider.ProfileId.ShouldBe(Guid.Empty);
    }

    [Fact]
    public void OwnershipKey_AuthenticatedCallerWithAMalformedOwner_IsNone()
    {
        var context = CreateAuthenticatedContext([new Claim("client_id", "onboarding-svc")]);
        context.Request.QueryString = new QueryString("?owner=%20%20");
        var provider = CreateProvider(context);

        provider.GetOwnershipKey().ShouldBeNull();
    }

    [Fact]
    public void ProfileId_AuthenticatedCallerWithAGuidSubject_IsThatSubject()
    {
        var subject = Guid.NewGuid();
        var provider = CreateProvider(CreateAuthenticatedContext([new Claim("sub", subject.ToString())]));

        provider.ProfileId.ShouldBe(subject);
    }

    #endregion
}
