using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using DKNet.StaticData.Share;

namespace DKNet.StaticData.App.Tests.Integration.Support;

/// <summary>
/// Puts a caller's <see cref="HttpContext"/> on the host's <see cref="IHttpContextAccessor"/> for the rest of the
/// calling method, so code run through a bare <see cref="IServiceScope"/> — seeding through the repository, or a
/// handler sent through the bus — resolves the same ownership key as the fixture's HTTP caller. Without it,
/// <c>PrincipalProvider</c> sees no request and resolves no key, so <c>DataOwnerAuthQuery</c> hides every
/// <c>IOwnedBy</c> row and <c>CoreDbContext</c> refuses to insert one (DRK-1901).
/// </summary>
/// <remarks>
/// Create it in the same method that seeds or reads (<c>using var caller = CallerHttpContext.System(...)</c>):
/// the accessor stores the context in an async-local, so a value set inside an awaited helper is gone once that
/// helper returns. Disposing restores the previous value.
/// </remarks>
public sealed class CallerHttpContext : IDisposable
{
    private readonly IHttpContextAccessor _accessor;
    private readonly HttpContext? _previous;

    private CallerHttpContext(IServiceProvider services, HttpContext context)
    {
        _accessor = services.GetRequiredService<IHttpContextAccessor>();
        _previous = _accessor.HttpContext;
        _accessor.HttpContext = context;
    }

    /// <summary>
    /// An unauthenticated caller, which <c>PrincipalProvider</c> resolves to <see cref="SharedConsts.SystemAccount"/> —
    /// the same key <see cref="ApiFixture"/>'s demonstration caller gets over HTTP.
    /// </summary>
    public static CallerHttpContext System(IServiceProvider services) => new(services, new DefaultHttpContext());

    /// <summary>An authenticated caller whose subject (<see cref="ClaimTypes.NameIdentifier"/>) is <paramref name="subject"/>.</summary>
    public static CallerHttpContext Subject(IServiceProvider services, string subject) =>
        new(services, new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, subject)], "Test"))
        });

    public void Dispose() => _accessor.HttpContext = _previous;
}
