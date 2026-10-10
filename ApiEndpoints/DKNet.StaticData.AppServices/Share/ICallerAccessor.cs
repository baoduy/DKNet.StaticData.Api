namespace DKNet.StaticData.AppServices.Share;

/// <summary>
///     The id of the calling application: the first non-blank of the token's <c>client_id</c>, <c>azp</c> and
///     <c>appid</c> claims (ADR-0016). It stamps the audit fields and names the caller in log entries. It is never
///     the owner.
/// </summary>
public interface ICallerAccessor
{
    #region Properties

    /// <summary>The caller id, or <see langword="null" /> when the call carries none.</summary>
    string? CallerId { get; }

    #endregion
}
