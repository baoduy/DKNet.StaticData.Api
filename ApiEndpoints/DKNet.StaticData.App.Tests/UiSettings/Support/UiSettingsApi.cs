using System.Text;
using DKNet.StaticData.App.Tests.Files.Support;

namespace DKNet.StaticData.App.Tests.UiSettings.Support;

/// <summary>
/// Drives the 5 UI setting routes and the erase route of spec DRK-2245 §3a on a <see cref="FilesApi"/> host, the way a
/// calling service does. Request bodies are written as raw JSON text, never through a serializer, so a value keeps its
/// exact bytes (spaces, key order, <c>1.0</c>, an unescaped <c>é</c>) and a missing field and a <c>null</c> field stay
/// different; <c>If-Match</c> is sent exactly as written.
/// </summary>
internal static class UiSettingsApi
{
    public const string Route = "/v1/ui-settings";

    public const string EraseRoute = "/v1/ui-settings/all";

    /// <summary>"a table layout value": the layout of a customer list screen.</summary>
    public const string TableLayout = """{"columns":["name","status"],"sort":"name","pageSize":50}""";

    /// <summary>A JSON request body, exactly as written.</summary>
    public static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    /// <summary>A JSON string literal of <paramref name="text"/>.</summary>
    public static string Text(string text) => JsonSerializer.Serialize(text);

    /// <summary>A group id as the JSON text of the <c>groupId</c> field.</summary>
    public static string Id(Guid id) => $"\"{id}\"";

    /// <summary>
    /// The setting body. <paramref name="settingKey"/> and <paramref name="settingType"/> are texts; <paramref name="value"/>,
    /// <paramref name="groupId"/> and <paramref name="schemaVersion"/> are raw JSON text, put into the body as they are.
    /// Each field is left out when null.
    /// </summary>
    public static string SettingBody(
        string? settingKey,
        string? settingType = "TableLayout",
        string? value = TableLayout,
        string? groupId = null,
        string? schemaVersion = null)
    {
        var fields = new List<string>();
        if (settingKey is not null)
        {
            fields.Add($"\"settingKey\":{Text(settingKey)}");
        }

        if (settingType is not null)
        {
            fields.Add($"\"settingType\":{Text(settingType)}");
        }

        if (groupId is not null)
        {
            fields.Add($"\"groupId\":{groupId}");
        }

        if (value is not null)
        {
            fields.Add($"\"value\":{value}");
        }

        if (schemaVersion is not null)
        {
            fields.Add($"\"schemaVersion\":{schemaVersion}");
        }

        return "{" + string.Join(",", fields) + "}";
    }

    public static Task<HttpResponseMessage> CreateSettingAsync(
        this FilesApi api,
        string? token,
        string? owner,
        string json,
        string? traceParent = null) =>
        api.SendAsync(HttpMethod.Post, Route + FilesApi.OwnerQuery(owner), token, Json(json), traceParent: traceParent);

    public static Task<HttpResponseMessage> ListSettingsAsync(
        this FilesApi api,
        string? token,
        string? owner,
        string moreQuery = "",
        string? traceParent = null) =>
        api.SendAsync(HttpMethod.Get, Route + FilesApi.OwnerQuery(owner) + moreQuery, token, traceParent: traceParent);

    public static Task<HttpResponseMessage> ReadSettingAsync(this FilesApi api, string? token, string owner, Guid settingId) =>
        api.SendAsync(HttpMethod.Get, $"{Route}/{settingId}{FilesApi.OwnerQuery(owner)}", token);

    public static Task<HttpResponseMessage> UpdateSettingAsync(
        this FilesApi api,
        string? token,
        string? owner,
        Guid settingId,
        string json,
        string? ifMatch,
        string? traceParent = null) =>
        api.SendAsync(
            HttpMethod.Put, $"{Route}/{settingId}{FilesApi.OwnerQuery(owner)}", token, Json(json),
            traceParent: traceParent, ifMatch: ifMatch);

    public static Task<HttpResponseMessage> DeleteSettingAsync(
        this FilesApi api,
        string? token,
        string owner,
        Guid settingId,
        string? ifMatch = null,
        string? traceParent = null) =>
        api.SendAsync(
            HttpMethod.Delete, $"{Route}/{settingId}{FilesApi.OwnerQuery(owner)}", token,
            traceParent: traceParent, ifMatch: ifMatch);

    public static Task<HttpResponseMessage> EraseSettingsAsync(
        this FilesApi api,
        string? token,
        string? owner,
        string? traceParent = null) =>
        api.SendAsync(HttpMethod.Delete, EraseRoute + FilesApi.OwnerQuery(owner), token, traceParent: traceParent);

    /// <summary>
    /// "Given owner &lt;owner&gt; has the setting &lt;settingKey&gt;": creates it as "layout-svc" with the type
    /// "TableLayout", requires 201 and returns its setting id.
    /// </summary>
    public static async Task<Guid> HasSettingAsync(
        this FilesApi api,
        string owner,
        string settingKey,
        Guid? groupId = null,
        string value = TableLayout,
        string? schemaVersion = null)
    {
        using var response = await api.CreateSettingAsync(Callers.LayoutSvc, owner,
            SettingBody(settingKey, value: value, groupId: groupId is { } id ? Id(id) : null, schemaVersion: schemaVersion));
        await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
        return (await response.JsonAsync()).GetProperty("settingId").GetGuid();
    }

    /// <summary>
    /// "… at version 2": sends the setting's own type, group and value back with <c>If-Match: "1"</c>, requiring 200 and
    /// version 2.
    /// </summary>
    public static async Task RaiseSettingToVersion2Async(
        this FilesApi api,
        string owner,
        Guid settingId,
        Guid? groupId = null,
        string value = TableLayout)
    {
        using var response = await api.UpdateSettingAsync(Callers.LayoutSvc, owner, settingId,
            SettingBody(null, value: value, groupId: groupId is { } id ? Id(id) : null), "\"1\"");
        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        (await response.JsonAsync()).GetProperty("version").GetInt32().ShouldBe(2);
    }

    /// <summary>The setting as one read answers it, as "layout-svc"; requires 200.</summary>
    public static async Task<JsonElement> SettingAsync(this FilesApi api, string owner, Guid settingId)
    {
        using var response = await api.ReadSettingAsync(Callers.LayoutSvc, owner, settingId);
        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        return await response.JsonAsync();
    }

    /// <summary>The setting keys of the owner's settings, newest first, read as "layout-svc"; requires 200.</summary>
    public static async Task<IReadOnlyList<string>> SettingKeysAsync(this FilesApi api, string owner, string moreQuery = "")
    {
        using var response = await api.ListSettingsAsync(Callers.LayoutSvc, owner, "&pageNumber=1&pageSize=100" + moreQuery);
        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        return SettingKeys(await response.JsonAsync());
    }

    /// <summary>The <c>settingKey</c> of every item of a setting list answer, in order.</summary>
    public static string[] SettingKeys(JsonElement page) =>
        [.. page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("settingKey").GetString()!)];

    /// <summary>The exact text of the <c>value</c> field as the answer wrote it, never a parsed object.</summary>
    public static string ValueText(this JsonElement setting) => setting.GetProperty("value").GetRawText();
}
