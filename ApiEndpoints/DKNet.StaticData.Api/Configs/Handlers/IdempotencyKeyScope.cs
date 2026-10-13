using DKNet.StaticData.Api.ApiEndpoints.Files;

namespace DKNet.StaticData.Api.Configs.Handlers;

/// <summary>
/// The scope an idempotency key lives in (ADR-0012): the caller id and the owner, then the package adds the route and
/// the key. The route template does not hold the owner, so without it a key reused for another owner would replay the
/// first owner's answer.
/// </summary>
internal static class IdempotencyKeyScope
{
    #region Methods

    /// <summary>
    /// <c>&lt;caller id length&gt;:&lt;caller id&gt;:&lt;owner&gt;</c>. The length prefix keeps every caller id and owner
    /// pair apart: caller <c>a</c> with owner <c>b:c</c> never shares a scope with caller <c>a:b</c> with owner
    /// <c>c</c>. The package keeps only a hash of it.
    /// </summary>
    public static string Resolve(HttpContext context)
    {
        var callerId = CallerAccessor.Read(context.User) ?? string.Empty;
        OwnerQuery.TryRead(context.Request, out var owner);
        return $"{callerId.Length}:{callerId}:{owner}";
    }

    #endregion
}
