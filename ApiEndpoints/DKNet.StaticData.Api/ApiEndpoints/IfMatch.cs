using System.Globalization;

namespace DKNet.StaticData.Api.ApiEndpoints;

/// <summary>
/// The <c>If-Match</c> header of a version-guarded update (ADR-0010): exactly one strong tag <c>"&lt;version&gt;"</c>,
/// as the <c>ETag</c> of every answer writes it.
/// </summary>
internal static class IfMatch
{
    #region Methods

    /// <returns>
    /// <see langword="null" /> when the header is missing; the version a strong tag <c>"n"</c> names; otherwise 0, which
    /// no record ever has — <c>*</c>, a weak tag, a list, an unquoted or a non-number tag never match.
    /// </returns>
    public static int? Read(HttpRequest request)
    {
        var values = request.Headers.IfMatch;
        if (values.Count == 0)
        {
            return null;
        }

        return values is [['"', .. var number, '"']]
               && int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var version)
            ? version
            : 0;
    }

    #endregion
}
