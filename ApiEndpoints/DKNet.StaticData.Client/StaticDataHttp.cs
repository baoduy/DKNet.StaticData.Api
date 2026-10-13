using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DKNet.StaticData.Client.Contracts;

namespace DKNet.StaticData.Client;

/// <summary>
/// The wire rules every <see cref="StaticDataFileClient"/> call shares: the query string, the version tag, the JSON
/// bodies and answers, and the typed error for a non-success answer.
/// </summary>
internal static class StaticDataHttp
{
    /// <summary>Case-insensitive, so a field the service renames only in casing still reads.</summary>
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>The page, then each date that is given, both written invariant in round-trip form.</summary>
    internal static List<(string Key, string Value)> PageQuery(
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
    internal static void AddVersion(HttpRequestMessage request, int version) =>
        request.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{version.ToString(CultureInfo.InvariantCulture)}\""));

    /// <summary><paramref name="path"/> with the owner first, then <paramref name="more"/>, every value URL-encoded.</summary>
    internal static string WithQuery(string path, string owner, params IReadOnlyList<(string Key, string Value)> more) =>
        $"{path}?owner={Uri.EscapeDataString(owner)}" +
        string.Concat(more.Select(p => $"&{p.Key}={Uri.EscapeDataString(p.Value)}"));

    /// <summary>
    /// <paramref name="body"/> as JSON, written now rather than while sending, so a value that is not JSON throws
    /// <see cref="JsonException"/> before any request goes out.
    /// </summary>
    internal static StringContent JsonBody<T>(T body) =>
        new(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");

    internal static async Task<T> ReadAsync<T>(this HttpClient httpClient, HttpRequestMessage request, CancellationToken ct)
        where T : new()
    {
        using var response = await httpClient.SendOrThrowAsync(request, HttpCompletionOption.ResponseContentRead, ct)
            .ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct).ConfigureAwait(false) ?? new T();
    }

    /// <summary>Sends <paramref name="request"/>; a non-success answer throws <see cref="StaticDataApiException"/>.</summary>
    internal static async Task<HttpResponseMessage> SendOrThrowAsync(
        this HttpClient httpClient,
        HttpRequestMessage request,
        HttpCompletionOption completion,
        CancellationToken ct)
    {
        var response = await httpClient.SendAsync(request, completion, ct).ConfigureAwait(false);
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
