using DKNet.StaticData.Client.Contracts;

namespace DKNet.StaticData.Client;

/// <summary>
/// One typed call per file and file group route of the DKNet StaticData service. Every call takes the owner the file
/// or group is kept under; the upload and the group create also take the idempotency key, and the link and the group
/// update take the version they send as <c>If-Match</c>. A non-success answer throws <see cref="StaticDataApiException"/>.
/// </summary>
public interface IStaticDataFileClient
{
    /// <summary>
    /// <c>POST /v1/files</c>: uploads one file for <paramref name="owner"/>, sending <paramref name="idempotencyKey"/> as
    /// the <c>X-Idempotency-Key</c> header, linked to <paramref name="groupId"/> when one is given. The client reads
    /// <paramref name="content"/> to its end and disposes it.
    /// </summary>
    Task<StaticDataFile> UploadAsync(
        string owner,
        string idempotencyKey,
        string fileName,
        Stream content,
        Guid? groupId = null,
        CancellationToken ct = default);

    /// <summary><c>GET /v1/files</c>: one page of the owner's files, newest first, only those of <paramref name="groupId"/> when one is given.</summary>
    Task<StaticDataFilePage> ListAsync(
        string owner,
        int pageNumber,
        int pageSize,
        DateTimeOffset? fromDate = null,
        DateTimeOffset? toDate = null,
        Guid? groupId = null,
        CancellationToken ct = default);

    /// <summary><c>GET /v1/files/{fileId}</c>: one file's details.</summary>
    Task<StaticDataFile> GetAsync(string owner, Guid fileId, CancellationToken ct = default);

    /// <summary><c>GET /v1/files/{fileId}/content</c>: the file's bytes, name and content type.</summary>
    Task<StaticDataFileContent> DownloadAsync(string owner, Guid fileId, CancellationToken ct = default);

    /// <summary><c>DELETE /v1/files/{fileId}</c>: deletes the file's details and bytes.</summary>
    Task DeleteAsync(string owner, Guid fileId, CancellationToken ct = default);

    /// <summary>
    /// <c>PUT /v1/files/{fileId}/group</c>: links the file to <paramref name="groupId"/>, or unlinks it when
    /// <paramref name="groupId"/> is <see langword="null"/>, sending <paramref name="version"/> as <c>If-Match</c>.
    /// Returns the file with its new version.
    /// </summary>
    Task<StaticDataFile> LinkAsync(string owner, Guid fileId, Guid? groupId, int version, CancellationToken ct = default);

    /// <summary>
    /// <c>POST /v1/file-groups</c>: creates a file group for <paramref name="owner"/>, sending
    /// <paramref name="idempotencyKey"/> as the <c>X-Idempotency-Key</c> header.
    /// </summary>
    Task<StaticDataFileGroup> CreateGroupAsync(
        string owner,
        string idempotencyKey,
        string name,
        string? purpose = null,
        string? externalRef = null,
        CancellationToken ct = default);

    /// <summary><c>GET /v1/file-groups</c>: one page of the owner's file groups, newest first.</summary>
    Task<StaticDataFileGroupPage> ListGroupsAsync(
        string owner,
        int pageNumber,
        int pageSize,
        DateTimeOffset? fromDate = null,
        DateTimeOffset? toDate = null,
        string? purpose = null,
        string? externalRef = null,
        CancellationToken ct = default);

    /// <summary><c>GET /v1/file-groups/{groupId}</c>: one file group.</summary>
    Task<StaticDataFileGroup> GetGroupAsync(string owner, Guid groupId, CancellationToken ct = default);

    /// <summary>
    /// <c>PUT /v1/file-groups/{groupId}</c>: replaces the group's name, purpose and external reference, sending
    /// <paramref name="version"/> as <c>If-Match</c>. Returns the group with its new version.
    /// </summary>
    Task<StaticDataFileGroup> UpdateGroupAsync(
        string owner,
        Guid groupId,
        int version,
        string name,
        string? purpose = null,
        string? externalRef = null,
        CancellationToken ct = default);

    /// <summary><c>DELETE /v1/file-groups/{groupId}</c>: deletes a group with no linked file.</summary>
    Task DeleteGroupAsync(string owner, Guid groupId, CancellationToken ct = default);
}
