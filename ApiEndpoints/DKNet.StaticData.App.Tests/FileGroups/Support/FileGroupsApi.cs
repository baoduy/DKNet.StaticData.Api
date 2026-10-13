using System.Text;
using DKNet.StaticData.App.Tests.Files.Support;

namespace DKNet.StaticData.App.Tests.FileGroups.Support;

/// <summary>
/// Drives the 5 file group routes and the link route of spec DRK-2235 §3a on a <see cref="FilesApi"/> host, the way a
/// calling service does. Request bodies are sent as the literal JSON a scenario names, so a missing field and a
/// <c>null</c> field stay different; <c>If-Match</c> is sent exactly as written.
/// </summary>
internal static class FileGroupsApi
{
    public const string Route = "/v1/file-groups";

    /// <summary>A JSON request body, exactly as written.</summary>
    public static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    /// <summary>The group body <c>{"name": …, "purpose": …, "externalRef": …}</c>, each field left out when null.</summary>
    public static string GroupBody(string? name, string? purpose = null, string? externalRef = null)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        if (name is not null)
        {
            fields["name"] = name;
        }

        if (purpose is not null)
        {
            fields["purpose"] = purpose;
        }

        if (externalRef is not null)
        {
            fields["externalRef"] = externalRef;
        }

        return JsonSerializer.Serialize(fields);
    }

    /// <summary>The link body <c>{"groupId":"…"}</c>.</summary>
    public static string LinkBody(Guid groupId) => $$"""{"groupId":"{{groupId}}"}""";

    /// <summary>The unlink body <c>{"groupId":null}</c>.</summary>
    public const string UnlinkBody = """{"groupId":null}""";

    public static Task<HttpResponseMessage> CreateGroupAsync(
        this FilesApi api,
        string? token,
        string? owner,
        string json,
        string? key,
        string? traceParent = null) =>
        api.SendAsync(HttpMethod.Post, Route + FilesApi.OwnerQuery(owner), token, Json(json), key, traceParent);

    public static Task<HttpResponseMessage> ListGroupsAsync(
        this FilesApi api,
        string? token,
        string? owner,
        string moreQuery = "",
        string? traceParent = null) =>
        api.SendAsync(HttpMethod.Get, Route + FilesApi.OwnerQuery(owner) + moreQuery, token, traceParent: traceParent);

    public static Task<HttpResponseMessage> ReadGroupAsync(this FilesApi api, string? token, string owner, Guid groupId) =>
        api.SendAsync(HttpMethod.Get, $"{Route}/{groupId}{FilesApi.OwnerQuery(owner)}", token);

    public static Task<HttpResponseMessage> UpdateGroupAsync(
        this FilesApi api,
        string? token,
        string? owner,
        Guid groupId,
        string json,
        string? ifMatch) =>
        api.SendAsync(HttpMethod.Put, $"{Route}/{groupId}{FilesApi.OwnerQuery(owner)}", token, Json(json), ifMatch: ifMatch);

    public static Task<HttpResponseMessage> DeleteGroupAsync(
        this FilesApi api,
        string? token,
        string owner,
        Guid groupId,
        string? ifMatch = null) =>
        api.SendAsync(HttpMethod.Delete, $"{Route}/{groupId}{FilesApi.OwnerQuery(owner)}", token, ifMatch: ifMatch);

    /// <summary><c>PUT /v1/files/{fileId}/group</c> with <paramref name="json"/> as the body.</summary>
    public static Task<HttpResponseMessage> LinkAsync(
        this FilesApi api,
        string? token,
        string owner,
        Guid fileId,
        string json,
        string? ifMatch,
        string? traceParent = null) =>
        api.SendAsync(
            HttpMethod.Put, $"{FilesApi.Route}/{fileId}/group{FilesApi.OwnerQuery(owner)}", token, Json(json),
            traceParent: traceParent, ifMatch: ifMatch);

    /// <summary>Uploads one file with one <c>groupId</c> form part per entry of <paramref name="groupIds"/>.</summary>
    public static Task<HttpResponseMessage> UploadIntoAsync(
        this FilesApi api,
        string? token,
        string owner,
        string fileName,
        string key,
        params string[] groupIds)
    {
        var form = FilesApi.Form((fileName, Bytes.Of(1024), "application/octet-stream"));
        foreach (var groupId in groupIds)
        {
            form.Add(new StringContent(groupId), "groupId");
        }

        return api.SendAsync(HttpMethod.Post, FilesApi.Route + FilesApi.OwnerQuery(owner), token, form, key);
    }

    /// <summary>
    /// "Given owner &lt;owner&gt; has the group &lt;name&gt;": creates it as "onboarding-svc" with a new key, requires 201
    /// and returns its group id.
    /// </summary>
    public static async Task<Guid> HasGroupAsync(this FilesApi api, string owner, string name, string? purpose = null, string? externalRef = null)
    {
        using var response = await api.CreateGroupAsync(
            Callers.OnboardingSvc, owner, GroupBody(name, purpose, externalRef), FilesApi.NewKey());
        await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
        return (await response.JsonAsync()).GetProperty("groupId").GetGuid();
    }

    /// <summary>"… at version 2": renames the group to its own name with <c>If-Match: "1"</c>, requiring 200 and version 2.</summary>
    public static async Task RaiseToVersion2Async(this FilesApi api, string owner, Guid groupId, string name)
    {
        using var response = await api.UpdateGroupAsync(Callers.OnboardingSvc, owner, groupId, GroupBody(name), "\"1\"");
        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        (await response.JsonAsync()).GetProperty("version").GetInt32().ShouldBe(2);
    }

    /// <summary>"Given … the file &lt;fileName&gt; linked": uploads it into the group as "onboarding-svc", requiring 201.</summary>
    public static async Task<Guid> HasFileInGroupAsync(this FilesApi api, string owner, string fileName, Guid groupId)
    {
        using var response = await api.UploadIntoAsync(Callers.OnboardingSvc, owner, fileName, FilesApi.NewKey(), groupId.ToString());
        await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
        var details = await response.JsonAsync();
        details.GetProperty("groupId").GetGuid().ShouldBe(groupId);
        return details.GetProperty("fileId").GetGuid();
    }

    /// <summary>Links the file to the group with <paramref name="ifMatch"/> as "onboarding-svc", requiring 200.</summary>
    public static async Task LinkedAsync(this FilesApi api, string owner, Guid fileId, Guid groupId, string ifMatch)
    {
        using var response = await api.LinkAsync(Callers.OnboardingSvc, owner, fileId, LinkBody(groupId), ifMatch);
        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
    }

    /// <summary>The names of the owner's groups, newest first, read as "onboarding-svc"; requires 200.</summary>
    public static async Task<IReadOnlyList<string>> GroupNamesAsync(this FilesApi api, string owner, string moreQuery = "")
    {
        using var response = await api.ListGroupsAsync(Callers.OnboardingSvc, owner, "&pageNumber=1&pageSize=100" + moreQuery);
        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        return Names(await response.JsonAsync(), "name");
    }

    /// <summary>The file names of the group's files, newest first, read as "onboarding-svc"; requires 200.</summary>
    public static async Task<string[]> GroupFileNamesAsync(this FilesApi api, string owner, Guid groupId)
    {
        using var response = await api.ListAsync(Callers.OnboardingSvc, owner, $"&pageNumber=1&pageSize=100&groupId={groupId}");
        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        return Names(await response.JsonAsync(), "fileName");
    }

    /// <summary>The group as one read answers it, as "onboarding-svc"; requires 200.</summary>
    public static async Task<JsonElement> GroupAsync(this FilesApi api, string owner, Guid groupId)
    {
        using var response = await api.ReadGroupAsync(Callers.OnboardingSvc, owner, groupId);
        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        return await response.JsonAsync();
    }

    /// <summary>The file's details as one read answers them, as "onboarding-svc"; requires 200.</summary>
    public static async Task<JsonElement> FileAsync(this FilesApi api, string owner, Guid fileId)
    {
        using var response = await api.ReadAsync(Callers.OnboardingSvc, owner, fileId);
        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        return await response.JsonAsync();
    }

    /// <summary>The <paramref name="field"/> of every item of a list answer, in order.</summary>
    public static string[] Names(JsonElement page, string field) =>
        [.. page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty(field).GetString()!)];

    /// <summary>Whether the JSON object carries <paramref name="field"/> at all.</summary>
    public static bool Has(this JsonElement json, string field) => json.TryGetProperty(field, out _);
}
