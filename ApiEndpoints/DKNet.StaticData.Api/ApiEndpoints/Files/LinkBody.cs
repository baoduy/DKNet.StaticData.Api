using System.Text.Json;

namespace DKNet.StaticData.Api.ApiEndpoints.Files;

/// <summary>
/// The body of a link request, <c>{ "groupId": "&lt;guid&gt;" }</c> or <c>{ "groupId": null }</c>, read from the raw
/// request: a bound <c>Guid?</c> cannot tell a missing field from <see langword="null" />, and a bound body answers 415
/// or a bodiless 400 before the owner is checked (brief DRK-2237 §9 Q1).
/// </summary>
internal static class LinkBody
{
    #region Methods

    /// <returns>
    /// Whether the body is a JSON object whose <c>groupId</c> is a group id or <see langword="null" />, and that group
    /// id. A missing field, any other value, an empty body or one that is not JSON is not valid.
    /// </returns>
    public static async Task<(bool IsValid, Guid? GroupId)> ReadAsync(Stream body, CancellationToken cancellationToken)
    {
        try
        {
            using var json = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
            if (json.RootElement.ValueKind != JsonValueKind.Object
                || !json.RootElement.TryGetProperty("groupId", out var field))
            {
                return (false, null);
            }

            return field.ValueKind switch
            {
                JsonValueKind.Null => (true, null),
                JsonValueKind.String when field.TryGetGuid(out var groupId) => (true, groupId),
                _ => (false, null)
            };
        }
        catch (JsonException)
        {
            return (false, null);
        }
    }

    #endregion
}
