using System.Net.Http.Headers;
using DKNet.StaticData.App.TestSupport;

namespace DKNet.StaticData.App.Tests.Files.Support;

/// <summary>
/// Drives the 5 file routes of one <see cref="FilesApiFactory"/> host over HTTP, the way a calling service does: a
/// bearer token, the <c>owner</c> query parameter, the <c>X-Idempotency-Key</c> header and a multipart form.
/// </summary>
public sealed class FilesApi(FilesApiFactory host, TestDatabaseServer server)
{
    public const string Route = "/v1/files";
    public const string IdempotencyKeyHeader = "X-Idempotency-Key";

    private HttpClient? _client;

    public FilesApiFactory Host => host;

    public TestDatabaseServer Server => server;

    private HttpClient Client => _client ??= host.CreateClient();

    /// <summary><c>?owner=</c> with the owner URL-encoded, or nothing when <paramref name="owner"/> is null.</summary>
    public static string OwnerQuery(string? owner) => owner is null ? string.Empty : $"?owner={Uri.EscapeDataString(owner)}";

    /// <summary>A multipart form with one <c>file</c> part per entry, each declaring <paramref name="files"/>' content type.</summary>
    public static MultipartFormDataContent Form(params (string FileName, byte[] Bytes, string DeclaredType)[] files)
    {
        var form = new MultipartFormDataContent();
        foreach (var (fileName, bytes, declaredType) in files)
        {
            var part = new ByteArrayContent(bytes);
            part.Headers.ContentType = MediaTypeHeaderValue.Parse(declaredType);
            form.Add(part, "file", fileName);
        }

        return form;
    }

    /// <summary>Uploads one file. A null <paramref name="key"/> sends no idempotency key; a null owner sends no owner.</summary>
    public Task<HttpResponseMessage> UploadAsync(
        string? token,
        string? owner,
        string fileName,
        byte[] bytes,
        string? key,
        string declaredType = "application/octet-stream") =>
        SendAsync(HttpMethod.Post, Route + OwnerQuery(owner), token, Form((fileName, bytes, declaredType)), key);

    public Task<HttpResponseMessage> ListAsync(string? token, string? owner, string moreQuery = "") =>
        SendAsync(HttpMethod.Get, Route + OwnerQuery(owner) + moreQuery, token);

    public Task<HttpResponseMessage> ReadAsync(string? token, string owner, Guid fileId) =>
        SendAsync(HttpMethod.Get, $"{Route}/{fileId}{OwnerQuery(owner)}", token);

    public Task<HttpResponseMessage> DownloadAsync(string? token, string owner, Guid fileId) =>
        SendAsync(HttpMethod.Get, $"{Route}/{fileId}/content{OwnerQuery(owner)}", token);

    public Task<HttpResponseMessage> DeleteAsync(string? token, string owner, Guid fileId) =>
        SendAsync(HttpMethod.Delete, $"{Route}/{fileId}{OwnerQuery(owner)}", token);

    /// <summary>
    /// Sends one request. <paramref name="token"/> null sends no <c>Authorization</c> header; <paramref name="traceParent"/>
    /// sets the W3C <c>traceparent</c> header the call's trace continues.
    /// </summary>
    public async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        string? token,
        HttpContent? content = null,
        string? idempotencyKey = null,
        string? traceParent = null)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (idempotencyKey is not null)
        {
            request.Headers.Add(IdempotencyKeyHeader, idempotencyKey);
        }

        if (traceParent is not null)
        {
            request.Headers.Add("traceparent", traceParent);
        }

        return await Client.SendAsync(request);
    }

    /// <summary>
    /// "Given owner &lt;owner&gt; has the file &lt;fileName&gt;": uploads it as "onboarding-svc" with a new key, requires 201
    /// and returns its file id.
    /// </summary>
    public async Task<Guid> HasFileAsync(string owner, string fileName, byte[]? bytes = null)
    {
        using var response = await UploadAsync(Callers.OnboardingSvc, owner, fileName, bytes ?? Bytes.Of(1024), NewKey());
        await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
        return (await response.JsonAsync()).GetProperty("fileId").GetGuid();
    }

    /// <summary>The file names the owner's list holds, newest first, read as "onboarding-svc"; requires 200.</summary>
    public async Task<IReadOnlyList<string>> FileNamesAsync(string owner)
    {
        using var response = await ListAsync(Callers.OnboardingSvc, owner, "&pageNumber=1&pageSize=100");
        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        return [.. (await response.JsonAsync()).GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("fileName").GetString()!)];
    }

    /// <summary>Every file the local folder holds, as a path relative to the folder with <c>/</c> separators.</summary>
    public IReadOnlyList<string> StoredBlobs() =>
        Directory.Exists(host.BlobRoot)
            ? [.. Directory.EnumerateFiles(host.BlobRoot, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(host.BlobRoot, f).Replace('\\', '/'))]
            : [];

    /// <summary>A fresh, well-formed idempotency key.</summary>
    public static string NewKey() => Guid.NewGuid().ToString("N");

    /// <summary>Empties the database tables, the blob folder and the log, and turns every seam off.</summary>
    internal async Task ResetAsync()
    {
        await server.DeleteAllRowsAsync(host.ConnectionString);
        if (Directory.Exists(host.BlobRoot))
        {
            Directory.Delete(host.BlobRoot, recursive: true);
        }

        Directory.CreateDirectory(host.BlobRoot);
        host.ResetSeams();
    }
}

/// <summary>Deterministic file content of a given size.</summary>
public static class Bytes
{
    public static byte[] Of(int size)
    {
        var bytes = new byte[size];
        for (var i = 0; i < size; i++)
        {
            bytes[i] = (byte)(i % 251);
        }

        return bytes;
    }
}

/// <summary>Assertions on an answer, each failing with the answer's status and body so a red run names why.</summary>
public static class AnswerAssertions
{
    public static async Task ShouldHaveStatusAsync(this HttpResponseMessage response, HttpStatusCode expected) =>
        response.StatusCode.ShouldBe(expected, await DescribeAsync(response));

    /// <summary>The status is <paramref name="expected"/> and the body is problem details carrying that status.</summary>
    public static async Task ShouldBeProblemDetailsAsync(this HttpResponseMessage response, HttpStatusCode expected)
    {
        await response.ShouldHaveStatusAsync(expected);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json", await DescribeAsync(response));
        (await response.JsonAsync()).GetProperty("status").GetInt32().ShouldBe((int)expected);
    }

    public static async Task<JsonElement> JsonAsync(this HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    public static async Task<string> DescribeAsync(HttpResponseMessage response) =>
        $"answer {(int)response.StatusCode} {response.StatusCode} from {response.RequestMessage?.Method} " +
        $"{response.RequestMessage?.RequestUri?.PathAndQuery}: {await response.Content.ReadAsStringAsync()}";
}
