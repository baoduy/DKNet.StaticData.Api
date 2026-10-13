using DKNet.StaticData.App.Tests.Files.Support;
using DKNet.StaticData.App.Tests.UiSettings.Support;

namespace DKNet.StaticData.App.Tests.SettingGroups.Support;

/// <summary>
/// Drives the 5 setting group routes of spec DRK-2245 §3a on a <see cref="FilesApi"/> host, the way a calling service
/// does. Bodies are sent as the literal JSON a scenario names; <c>If-Match</c> is sent exactly as written.
/// </summary>
internal static class SettingGroupsApi
{
    public const string Route = "/v1/ui-setting-groups";

    /// <summary>The group body <c>{"name": …, "description": …}</c>, each field left out when null.</summary>
    public static string GroupBody(string? name, string? description = null)
    {
        var fields = new List<string>();
        if (name is not null)
        {
            fields.Add($"\"name\":{UiSettingsApi.Text(name)}");
        }

        if (description is not null)
        {
            fields.Add($"\"description\":{UiSettingsApi.Text(description)}");
        }

        return "{" + string.Join(",", fields) + "}";
    }

    public static Task<HttpResponseMessage> CreateSettingGroupAsync(
        this FilesApi api,
        string? token,
        string? owner,
        string json,
        string? traceParent = null) =>
        api.SendAsync(HttpMethod.Post, Route + FilesApi.OwnerQuery(owner), token, UiSettingsApi.Json(json), traceParent: traceParent);

    public static Task<HttpResponseMessage> ListSettingGroupsAsync(this FilesApi api, string? token, string? owner, string moreQuery = "") =>
        api.SendAsync(HttpMethod.Get, Route + FilesApi.OwnerQuery(owner) + moreQuery, token);

    public static Task<HttpResponseMessage> ReadSettingGroupAsync(this FilesApi api, string? token, string owner, Guid groupId) =>
        api.SendAsync(HttpMethod.Get, $"{Route}/{groupId}{FilesApi.OwnerQuery(owner)}", token);

    public static Task<HttpResponseMessage> UpdateSettingGroupAsync(
        this FilesApi api,
        string? token,
        string? owner,
        Guid groupId,
        string json,
        string? ifMatch,
        string? traceParent = null) =>
        api.SendAsync(
            HttpMethod.Put, $"{Route}/{groupId}{FilesApi.OwnerQuery(owner)}", token, UiSettingsApi.Json(json),
            traceParent: traceParent, ifMatch: ifMatch);

    public static Task<HttpResponseMessage> DeleteSettingGroupAsync(
        this FilesApi api,
        string? token,
        string owner,
        Guid groupId,
        string? ifMatch = null,
        string? traceParent = null) =>
        api.SendAsync(
            HttpMethod.Delete, $"{Route}/{groupId}{FilesApi.OwnerQuery(owner)}", token,
            traceParent: traceParent, ifMatch: ifMatch);

    /// <summary>
    /// "Given owner &lt;owner&gt; has the group &lt;name&gt;": creates the setting group as "layout-svc", requires 201 and
    /// returns its group id.
    /// </summary>
    public static async Task<Guid> HasSettingGroupAsync(this FilesApi api, string owner, string name, string? description = null)
    {
        using var response = await api.CreateSettingGroupAsync(Callers.LayoutSvc, owner, GroupBody(name, description));
        await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
        return (await response.JsonAsync()).GetProperty("groupId").GetGuid();
    }

    /// <summary>"… at version 2": sends the group's own name back with <c>If-Match: "1"</c>, requiring 200 and version 2.</summary>
    public static async Task RaiseSettingGroupToVersion2Async(this FilesApi api, string owner, Guid groupId, string name)
    {
        using var response = await api.UpdateSettingGroupAsync(Callers.LayoutSvc, owner, groupId, GroupBody(name), "\"1\"");
        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        (await response.JsonAsync()).GetProperty("version").GetInt32().ShouldBe(2);
    }

    /// <summary>The setting group as one read answers it, as "layout-svc"; requires 200.</summary>
    public static async Task<JsonElement> SettingGroupAsync(this FilesApi api, string owner, Guid groupId)
    {
        using var response = await api.ReadSettingGroupAsync(Callers.LayoutSvc, owner, groupId);
        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        return await response.JsonAsync();
    }

    /// <summary>The names of the owner's setting groups, newest first, read as "layout-svc"; requires 200.</summary>
    public static async Task<IReadOnlyList<string>> SettingGroupNamesAsync(this FilesApi api, string owner, string moreQuery = "")
    {
        using var response = await api.ListSettingGroupsAsync(Callers.LayoutSvc, owner, "&pageNumber=1&pageSize=100" + moreQuery);
        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        return Names(await response.JsonAsync());
    }

    /// <summary>The <c>name</c> of every item of a setting group list answer, in order.</summary>
    public static string[] Names(JsonElement page) =>
        [.. page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("name").GetString()!)];
}
