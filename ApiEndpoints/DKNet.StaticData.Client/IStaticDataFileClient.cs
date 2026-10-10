using DKNet.StaticData.Client.Contracts;

namespace DKNet.StaticData.Client;

/// <summary>
/// One typed call per file route of the DKNet StaticData service. Every call takes the owner the file is kept
/// under; the upload also takes the idempotency key. A non-success answer throws <see cref="StaticDataApiException"/>.
/// </summary>
public interface IStaticDataFileClient
{
    /// <summary><c>POST /v1/files</c>: uploads one file for <paramref name="owner"/>.</summary>
    Task<StaticDataFile> UploadAsync(
        string owner,
        string idempotencyKey,
        string fileName,
        Stream content,
        CancellationToken ct = default);

    /// <summary><c>GET /v1/files</c>: one page of the owner's files, newest first.</summary>
    Task<StaticDataFilePage> ListAsync(
        string owner,
        int pageNumber,
        int pageSize,
        DateTimeOffset? fromDate = null,
        DateTimeOffset? toDate = null,
        CancellationToken ct = default);

    /// <summary><c>GET /v1/files/{fileId}</c>: one file's details.</summary>
    Task<StaticDataFile> GetAsync(string owner, Guid fileId, CancellationToken ct = default);

    /// <summary><c>GET /v1/files/{fileId}/content</c>: the file's bytes, name and content type.</summary>
    Task<StaticDataFileContent> DownloadAsync(string owner, Guid fileId, CancellationToken ct = default);

    /// <summary><c>DELETE /v1/files/{fileId}</c>: deletes the file's details and bytes.</summary>
    Task DeleteAsync(string owner, Guid fileId, CancellationToken ct = default);
}
