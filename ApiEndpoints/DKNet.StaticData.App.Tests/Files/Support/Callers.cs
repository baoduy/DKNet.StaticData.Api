using DKNet.StaticData.App.TestSupport;

namespace DKNet.StaticData.App.Tests.Files.Support;

/// <summary>
/// The callers spec DRK-2206 §5 names, as signed bearer tokens: "onboarding-svc" holds both app roles, "report-svc"
/// only <see cref="Read"/>, "ingest-svc" only <see cref="Write"/>. Each caller id travels in the token's
/// <c>client_id</c> claim unless a scenario names another claim.
/// </summary>
internal static class Callers
{
    public const string Read = "staticdata.read";
    public const string Write = "staticdata.write";

    public static string OnboardingSvc => Holding("onboarding-svc", Read, Write);

    public static string ReportSvc => Holding("report-svc", Read);

    public static string IngestSvc => Holding("ingest-svc", Write);

    /// <summary>A token with caller id <paramref name="callerId"/> in <c>client_id</c> and exactly <paramref name="roles"/> in <c>roles</c>.</summary>
    public static string Holding(string callerId, params string[] roles)
    {
        var claims = new Dictionary<string, object>(StringComparer.Ordinal) { ["client_id"] = callerId };
        if (roles.Length > 0)
        {
            claims["roles"] = roles;
        }

        return TestTokens.Mint(claims);
    }
}
