using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using DKNet.StaticData.Client.Contracts;

namespace DKNet.StaticData.Client;

/// <summary>
/// Default <see cref="IStaticDataFileClient"/> over an already-configured <see cref="HttpClient"/>. Never gets,
/// keeps or logs a credential of its own: every request carries only what the application's handlers add.
/// </summary>
public sealed class StaticDataFileClient : IStaticDataFileClient
{
    private const string Route = "/v1/files";

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
        CancellationToken ct = default)
    {
        // The service refuses a form with any part other than the one "file" part.
        using var file = new StreamContent(content);
        using var form = new MultipartFormDataContent();
        form.Add(file, "file", fileName);
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
        CancellationToken ct = default)
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
}
