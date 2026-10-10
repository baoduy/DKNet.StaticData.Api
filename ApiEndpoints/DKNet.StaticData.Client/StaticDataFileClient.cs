using DKNet.StaticData.Client.Contracts;

namespace DKNet.StaticData.Client;

/// <summary>
/// Default <see cref="IStaticDataFileClient"/> over an already-configured <see cref="HttpClient"/>. Never gets,
/// keeps or logs a credential of its own: every request carries only what the application's handlers add.
/// </summary>
public sealed class StaticDataFileClient : IStaticDataFileClient
{
    public StaticDataFileClient(HttpClient httpClient) => ArgumentNullException.ThrowIfNull(httpClient);

    public Task<StaticDataFile> UploadAsync(
        string owner,
        string idempotencyKey,
        string fileName,
        Stream content,
        CancellationToken ct = default) =>
        throw new NotImplementedException();

    public Task<StaticDataFilePage> ListAsync(
        string owner,
        int pageNumber,
        int pageSize,
        DateTimeOffset? fromDate = null,
        DateTimeOffset? toDate = null,
        CancellationToken ct = default) =>
        throw new NotImplementedException();

    public Task<StaticDataFile> GetAsync(string owner, Guid fileId, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public Task<StaticDataFileContent> DownloadAsync(string owner, Guid fileId, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public Task DeleteAsync(string owner, Guid fileId, CancellationToken ct = default) =>
        throw new NotImplementedException();
}
