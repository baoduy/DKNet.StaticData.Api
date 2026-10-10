namespace DKNet.StaticData.Api.Configs.Handlers;

/// <summary>
/// Reads the caller id from the authenticated caller's token: the first non-blank of <c>client_id</c> (a client
/// credentials token), <c>azp</c> (an Entra ID v2.0 token) and <c>appid</c> (a v1.0 token) — ADR-0016. Mirrors
/// DKNet.Accounts.Api's <c>CallingSystemAccessor</c>.
/// </summary>
internal sealed class CallerAccessor(IHttpContextAccessor accessor) : ICallerAccessor
{
    #region Fields

    private static readonly string[] CallerIdClaimTypes = ["client_id", "azp", "appid"];

    #endregion

    #region Properties

    public string? CallerId => Read(accessor.HttpContext?.User);

    #endregion

    #region Methods

    /// <summary>The caller id <paramref name="user" /> carries, or <see langword="null" /> when it carries none.</summary>
    public static string? Read(ClaimsPrincipal? user) =>
        user is null
            ? null
            : CallerIdClaimTypes
                .Select(type => user.FindFirst(type)?.Value)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    #endregion
}
