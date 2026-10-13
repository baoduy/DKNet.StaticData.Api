using System.Net.Http.Json;
using System.Text.Json.Serialization;
using DKNet.StaticData.Client.Contracts;
using static DKNet.StaticData.Client.StaticDataHttp;

namespace DKNet.StaticData.Client;

/// <summary>
/// Default <see cref="IStaticDataFileClient"/> over an already-configured <see cref="HttpClient"/>. Never gets,
/// keeps or logs a credential of its own: every request carries only what the application's handlers add.
/// </summary>
public sealed class StaticDataFileClient : IStaticDataFileClient
{
    private const string Route = "/v1/files";

    private const string GroupRoute = "/v1/file-groups";

    private const string SettingRoute = "/v1/ui-settings";

    private const string SettingGroupRoute = "/v1/ui-setting-groups";

    /// <summary>DKNet.AspCore.Idempotency's default header name, which the service keeps.</summary>
    private const string IdempotencyKeyHeader = "X-Idempotency-Key";

    private readonly HttpClient _httpClient;

    public StaticDataFileClient(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
    }

    public async Task<StaticDataFile> UploadAsync(
        string owner,
        string idempotencyKey,
        string fileName,
        Stream content,
        Guid? groupId = null,
        CancellationToken ct = default)
    {
        // The service refuses a form with any part other than the one "file" part and an optional "groupId" part.
        using var file = new StreamContent(content);
        using var group = groupId is { } id ? new StringContent(id.ToString()) : null;
        using var form = new MultipartFormDataContent();
        form.Add(file, "file", fileName);
        if (group is not null)
        {
            form.Add(group, "groupId");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, WithQuery(Route, owner)) { Content = form };
        request.Headers.Add(IdempotencyKeyHeader, idempotencyKey);
        return await _httpClient.ReadAsync<StaticDataFile>(request, ct).ConfigureAwait(false);
    }

    public async Task<StaticDataFilePage> ListAsync(
        string owner,
        int pageNumber,
        int pageSize,
        DateTimeOffset? fromDate = null,
        DateTimeOffset? toDate = null,
        Guid? groupId = null,
        CancellationToken ct = default)
    {
        var parameters = PageQuery(pageNumber, pageSize, fromDate, toDate);
        if (groupId is { } group)
        {
            parameters.Add(("groupId", group.ToString()));
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, WithQuery(Route, owner, parameters));
        return await _httpClient.ReadAsync<StaticDataFilePage>(request, ct).ConfigureAwait(false);
    }

    public async Task<StaticDataFile> GetAsync(string owner, Guid fileId, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, WithQuery($"{Route}/{fileId}", owner));
        return await _httpClient.ReadAsync<StaticDataFile>(request, ct).ConfigureAwait(false);
    }

    public async Task<StaticDataFileContent> DownloadAsync(string owner, Guid fileId, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, WithQuery($"{Route}/{fileId}/content", owner));
        // Headers only, so the bytes stream to the caller instead of being buffered here.
        var response = await _httpClient.SendOrThrowAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        var headers = response.Content.Headers;
        // filename* holds the exact UTF-8 name; filename is its quoted ASCII fallback.
        var fileName = headers.ContentDisposition?.FileNameStar ?? headers.ContentDisposition?.FileName?.Trim('"');
        // Disposing the stream releases the response.
        var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return new StaticDataFileContent(stream, fileName ?? string.Empty, headers.ContentType?.MediaType ?? string.Empty);
    }

    public async Task DeleteAsync(string owner, Guid fileId, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, WithQuery($"{Route}/{fileId}", owner));
        using var response = await _httpClient.SendOrThrowAsync(request, HttpCompletionOption.ResponseContentRead, ct)
            .ConfigureAwait(false);
    }

    public async Task<StaticDataFile> LinkAsync(
        string owner,
        Guid fileId,
        Guid? groupId,
        int version,
        CancellationToken ct = default)
    {
        // The service needs the field present, so a null group goes out as "groupId":null and unlinks the file.
        using var request = new HttpRequestMessage(HttpMethod.Put, WithQuery($"{Route}/{fileId}/group", owner))
        {
            Content = JsonContent.Create(new LinkBody(groupId), options: JsonOptions)
        };
        AddVersion(request, version);
        return await _httpClient.ReadAsync<StaticDataFile>(request, ct).ConfigureAwait(false);
    }

    public async Task<StaticDataFileGroup> CreateGroupAsync(
        string owner,
        string idempotencyKey,
        string name,
        string? purpose = null,
        string? externalRef = null,
        CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, WithQuery(GroupRoute, owner))
        {
            Content = JsonContent.Create(new GroupBody(name, purpose, externalRef), options: JsonOptions)
        };
        request.Headers.Add(IdempotencyKeyHeader, idempotencyKey);
        return await _httpClient.ReadAsync<StaticDataFileGroup>(request, ct).ConfigureAwait(false);
    }

    public async Task<StaticDataFileGroupPage> ListGroupsAsync(
        string owner,
        int pageNumber,
        int pageSize,
        DateTimeOffset? fromDate = null,
        DateTimeOffset? toDate = null,
        string? purpose = null,
        string? externalRef = null,
        CancellationToken ct = default)
    {
        var parameters = PageQuery(pageNumber, pageSize, fromDate, toDate);
        if (purpose is not null)
        {
            parameters.Add(("purpose", purpose));
        }

        if (externalRef is not null)
        {
            parameters.Add(("externalRef", externalRef));
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, WithQuery(GroupRoute, owner, parameters));
        return await _httpClient.ReadAsync<StaticDataFileGroupPage>(request, ct).ConfigureAwait(false);
    }

    public async Task<StaticDataFileGroup> GetGroupAsync(string owner, Guid groupId, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, WithQuery($"{GroupRoute}/{groupId}", owner));
        return await _httpClient.ReadAsync<StaticDataFileGroup>(request, ct).ConfigureAwait(false);
    }

    public async Task<StaticDataFileGroup> UpdateGroupAsync(
        string owner,
        Guid groupId,
        int version,
        string name,
        string? purpose = null,
        string? externalRef = null,
        CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, WithQuery($"{GroupRoute}/{groupId}", owner))
        {
            Content = JsonContent.Create(new GroupBody(name, purpose, externalRef), options: JsonOptions)
        };
        AddVersion(request, version);
        return await _httpClient.ReadAsync<StaticDataFileGroup>(request, ct).ConfigureAwait(false);
    }

    public async Task DeleteGroupAsync(string owner, Guid groupId, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, WithQuery($"{GroupRoute}/{groupId}", owner));
        using var response = await _httpClient.SendOrThrowAsync(request, HttpCompletionOption.ResponseContentRead, ct)
            .ConfigureAwait(false);
    }

    public async Task<StaticDataUiSetting> CreateSettingAsync(
        string owner,
        string settingKey,
        string settingType,
        string value,
        Guid? groupId = null,
        int? schemaVersion = null,
        CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, WithQuery(SettingRoute, owner))
        {
            Content = JsonBody(new SettingBody(settingKey, settingType, groupId, value, schemaVersion))
        };
        return await _httpClient.ReadAsync<StaticDataUiSetting>(request, ct).ConfigureAwait(false);
    }

    public async Task<StaticDataUiSettingPage> ListSettingsAsync(
        string owner,
        int pageNumber,
        int pageSize,
        DateTimeOffset? fromDate = null,
        DateTimeOffset? toDate = null,
        string? settingKey = null,
        Guid? groupId = null,
        CancellationToken ct = default)
    {
        var parameters = PageQuery(pageNumber, pageSize, fromDate, toDate);
        if (settingKey is not null)
        {
            parameters.Add(("settingKey", settingKey));
        }

        if (groupId is { } group)
        {
            parameters.Add(("groupId", group.ToString()));
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, WithQuery(SettingRoute, owner, parameters));
        return await _httpClient.ReadAsync<StaticDataUiSettingPage>(request, ct).ConfigureAwait(false);
    }

    public async Task<StaticDataUiSetting> GetSettingAsync(string owner, Guid settingId, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, WithQuery($"{SettingRoute}/{settingId}", owner));
        return await _httpClient.ReadAsync<StaticDataUiSetting>(request, ct).ConfigureAwait(false);
    }

    public async Task<StaticDataUiSetting> UpdateSettingAsync(
        string owner,
        Guid settingId,
        int version,
        string settingType,
        string value,
        Guid? groupId = null,
        int? schemaVersion = null,
        CancellationToken ct = default)
    {
        // The service ignores a setting key on update, so none is sent.
        using var request = new HttpRequestMessage(HttpMethod.Put, WithQuery($"{SettingRoute}/{settingId}", owner))
        {
            Content = JsonBody(new SettingBody(null, settingType, groupId, value, schemaVersion))
        };
        AddVersion(request, version);
        return await _httpClient.ReadAsync<StaticDataUiSetting>(request, ct).ConfigureAwait(false);
    }

    public async Task DeleteSettingAsync(string owner, Guid settingId, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, WithQuery($"{SettingRoute}/{settingId}", owner));
        using var response = await _httpClient.SendOrThrowAsync(request, HttpCompletionOption.ResponseContentRead, ct)
            .ConfigureAwait(false);
    }

    public async Task EraseSettingsAsync(string owner, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, WithQuery($"{SettingRoute}/all", owner));
        using var response = await _httpClient.SendOrThrowAsync(request, HttpCompletionOption.ResponseContentRead, ct)
            .ConfigureAwait(false);
    }

    public async Task<StaticDataSettingGroup> CreateSettingGroupAsync(
        string owner,
        string name,
        string? description = null,
        CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, WithQuery(SettingGroupRoute, owner))
        {
            Content = JsonContent.Create(new SettingGroupBody(name, description), options: JsonOptions)
        };
        return await _httpClient.ReadAsync<StaticDataSettingGroup>(request, ct).ConfigureAwait(false);
    }

    public async Task<StaticDataSettingGroupPage> ListSettingGroupsAsync(
        string owner,
        int pageNumber,
        int pageSize,
        DateTimeOffset? fromDate = null,
        DateTimeOffset? toDate = null,
        CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, WithQuery(SettingGroupRoute, owner, PageQuery(pageNumber, pageSize, fromDate, toDate)));
        return await _httpClient.ReadAsync<StaticDataSettingGroupPage>(request, ct).ConfigureAwait(false);
    }

    public async Task<StaticDataSettingGroup> GetSettingGroupAsync(string owner, Guid groupId, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, WithQuery($"{SettingGroupRoute}/{groupId}", owner));
        return await _httpClient.ReadAsync<StaticDataSettingGroup>(request, ct).ConfigureAwait(false);
    }

    public async Task<StaticDataSettingGroup> UpdateSettingGroupAsync(
        string owner,
        Guid groupId,
        int version,
        string name,
        string? description = null,
        CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, WithQuery($"{SettingGroupRoute}/{groupId}", owner))
        {
            Content = JsonContent.Create(new SettingGroupBody(name, description), options: JsonOptions)
        };
        AddVersion(request, version);
        return await _httpClient.ReadAsync<StaticDataSettingGroup>(request, ct).ConfigureAwait(false);
    }

    public async Task DeleteSettingGroupAsync(string owner, Guid groupId, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, WithQuery($"{SettingGroupRoute}/{groupId}", owner));
        using var response = await _httpClient.SendOrThrowAsync(request, HttpCompletionOption.ResponseContentRead, ct)
            .ConfigureAwait(false);
    }

    /// <summary>The group create and update body; an empty purpose or external reference is left out.</summary>
    private sealed record GroupBody(
        string Name,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Purpose,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ExternalRef);

    /// <summary>The link body; <see cref="GroupId"/> is always written, <see langword="null"/> included.</summary>
    private sealed record LinkBody(Guid? GroupId);

    /// <summary>
    /// The setting create and update body: <see cref="Value"/> goes out as its own JSON text, and an empty setting key
    /// (on update), group or schema version is left out, so the service takes no group and schema version 1.
    /// </summary>
    private sealed record SettingBody(
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SettingKey,
        string SettingType,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? GroupId,
        [property: JsonConverter(typeof(RawJsonConverter))] string Value,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? SchemaVersion);

    /// <summary>The setting group create and update body; an empty description is left out.</summary>
    private sealed record SettingGroupBody(
        string Name,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Description);
}
