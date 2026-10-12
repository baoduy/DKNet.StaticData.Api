using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DKNet.StaticData.Client.Contracts;

namespace DKNet.StaticData.Client;

/// <summary>
/// Default <see cref="IStaticDataFileClient"/> over an already-configured <see cref="HttpClient"/>. Never gets,
/// keeps or logs a credential of its own: every request carries only what the application's handlers add.
/// </summary>
public sealed class StaticDataFileClient : IStaticDataFileClient
{
    private const string Route = "/v1/files";

    private const string GroupRoute = "/v1/file-groups";

    /// <summary>DKNet.AspCore.Idempotency's default header name, which the service keeps.</summary>
    private const string IdempotencyKeyHeader = "X-Idempotency-Key";

    /// <summary>Case-insensitive, so a field the service renames only in casing still reads.</summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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
        return await ReadAsync<StaticDataFile>(request, ct).ConfigureAwait(false);
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
        return await ReadAsync<StaticDataFilePage>(request, ct).ConfigureAwait(false);
    }

    public async Task<StaticDataFile> GetAsync(string owner, Guid fileId, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, WithQuery($"{Route}/{fileId}", owner));
        return await ReadAsync<StaticDataFile>(request, ct).ConfigureAwait(false);
    }

    public async Task<StaticDataFileContent> DownloadAsync(string owner, Guid fileId, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, WithQuery($"{Route}/{fileId}/content", owner));
        // Headers only, so the bytes stream to the caller instead of being buffered here.
        var response = await SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
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
        using var response = await SendAsync(request, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
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
        return await ReadAsync<StaticDataFile>(request, ct).ConfigureAwait(false);
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
        return await ReadAsync<StaticDataFileGroup>(request, ct).ConfigureAwait(false);
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
        return await ReadAsync<StaticDataFileGroupPage>(request, ct).ConfigureAwait(false);
    }

    public async Task<StaticDataFileGroup> GetGroupAsync(string owner, Guid groupId, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, WithQuery($"{GroupRoute}/{groupId}", owner));
        return await ReadAsync<StaticDataFileGroup>(request, ct).ConfigureAwait(false);
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
        return await ReadAsync<StaticDataFileGroup>(request, ct).ConfigureAwait(false);
    }

    public async Task DeleteGroupAsync(string owner, Guid groupId, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, WithQuery($"{GroupRoute}/{groupId}", owner));
        using var response = await SendAsync(request, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
    }

    /// <summary>The page, then each date that is given, both written invariant in round-trip form.</summary>
    private static List<(string Key, string Value)> PageQuery(
        int pageNumber,
        int pageSize,
        DateTimeOffset? fromDate,
        DateTimeOffset? toDate)
    {
        var parameters = new List<(string Key, string Value)>
        {
            ("pageNumber", pageNumber.ToString(CultureInfo.InvariantCulture)),
            ("pageSize", pageSize.ToString(CultureInfo.InvariantCulture))
        };
        if (fromDate is { } from)
        {
            parameters.Add(("fromDate", from.ToString("O", CultureInfo.InvariantCulture)));
        }

        if (toDate is { } to)
        {
            parameters.Add(("toDate", to.ToString("O", CultureInfo.InvariantCulture)));
        }

        return parameters;
    }

    /// <summary>Sends <paramref name="version"/> as the one strong tag <c>If-Match: "&lt;version&gt;"</c> the service matches.</summary>
    private static void AddVersion(HttpRequestMessage request, int version) =>
        request.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{version.ToString(CultureInfo.InvariantCulture)}\""));

    /// <summary><paramref name="path"/> with the owner first, then <paramref name="more"/>, every value URL-encoded.</summary>
    private static string WithQuery(string path, string owner, params IReadOnlyList<(string Key, string Value)> more) =>
        $"{path}?owner={Uri.EscapeDataString(owner)}" +
        string.Concat(more.Select(p => $"&{p.Key}={Uri.EscapeDataString(p.Value)}"));

    private async Task<T> ReadAsync<T>(HttpRequestMessage request, CancellationToken ct)
        where T : new()
    {
        using var response = await SendAsync(request, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct).ConfigureAwait(false) ?? new T();
    }

    /// <summary>Sends <paramref name="request"/>; a non-success answer throws <see cref="StaticDataApiException"/>.</summary>
    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        HttpCompletionOption completion,
        CancellationToken ct)
    {
        var response = await _httpClient.SendAsync(request, completion, ct).ConfigureAwait(false);
        return response.IsSuccessStatusCode ? response : throw await ErrorOfAsync(response, ct).ConfigureAwait(false);
    }

    /// <summary>The typed error for a non-success <paramref name="response"/>, which it disposes.</summary>
    private static async Task<StaticDataApiException> ErrorOfAsync(HttpResponseMessage response, CancellationToken ct)
    {
        using (response)
        {
            return new StaticDataApiException(
                response.StatusCode,
                await ReadProblemDetailsAsync(response.Content, ct).ConfigureAwait(false),
                $"The StaticData service answered {(int)response.StatusCode} {response.ReasonPhrase}.");
        }
    }

    /// <summary>The body's problem details, or <see langword="null"/> when it is empty, not JSON or not problem details.</summary>
    private static async Task<StaticDataProblemDetails?> ReadProblemDetailsAsync(HttpContent content, CancellationToken ct)
    {
        try
        {
            var details = await content.ReadFromJsonAsync<StaticDataProblemDetails>(JsonOptions, ct).ConfigureAwait(false);
            return details is { Type: not null } or { Title: not null } or { Status: not null } ? details : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The group create and update body; an empty purpose or external reference is left out.</summary>
    private sealed record GroupBody(
        string Name,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Purpose,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ExternalRef);

    /// <summary>The link body; <see cref="GroupId"/> is always written, <see langword="null"/> included.</summary>
    private sealed record LinkBody(Guid? GroupId);
}
