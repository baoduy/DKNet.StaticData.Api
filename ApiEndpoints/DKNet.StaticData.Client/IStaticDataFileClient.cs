using DKNet.StaticData.Client.Contracts;

namespace DKNet.StaticData.Client;

/// <summary>
/// One typed call per file, file group, UI setting and setting group route of the DKNet StaticData service. Every call
/// takes the owner the record is kept under; the upload and the file group create also take the idempotency key, and
/// the link and every update take the version they send as <c>If-Match</c>. A non-success answer throws <see cref="StaticDataApiException"/>.
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

    /// <summary>
    /// <c>POST /v1/ui-settings</c>: creates the setting <paramref name="settingKey"/> for <paramref name="owner"/>.
    /// <paramref name="value"/> is raw JSON text, sent exactly as given; text that is not one JSON value throws
    /// <see cref="System.Text.Json.JsonException"/> before any request goes out. A <see langword="null"/>
    /// <paramref name="schemaVersion"/> is left out, so the service takes 1.
    /// </summary>
    Task<StaticDataUiSetting> CreateSettingAsync(
        string owner,
        string settingKey,
        string settingType,
        string value,
        Guid? groupId = null,
        int? schemaVersion = null,
        CancellationToken ct = default);

    /// <summary>
    /// <c>GET /v1/ui-settings</c>: one page of the owner's settings, newest first, values included; only those of
    /// <paramref name="settingKey"/> and <paramref name="groupId"/> when they are given.
    /// </summary>
    Task<StaticDataUiSettingPage> ListSettingsAsync(
        string owner,
        int pageNumber,
        int pageSize,
        DateTimeOffset? fromDate = null,
        DateTimeOffset? toDate = null,
        string? settingKey = null,
        Guid? groupId = null,
        CancellationToken ct = default);

    /// <summary><c>GET /v1/ui-settings/{settingId}</c>: one setting.</summary>
    Task<StaticDataUiSetting> GetSettingAsync(string owner, Guid settingId, CancellationToken ct = default);

    /// <summary>
    /// <c>PUT /v1/ui-settings/{settingId}</c>: replaces the setting's type, group, value and schema version, sending
    /// <paramref name="version"/> as <c>If-Match</c>. Returns the setting with its new version. <paramref name="value"/>
    /// and <paramref name="schemaVersion"/> go out as on <see cref="CreateSettingAsync"/>; a <see langword="null"/>
    /// <paramref name="groupId"/> unlinks the setting.
    /// </summary>
    Task<StaticDataUiSetting> UpdateSettingAsync(
        string owner,
        Guid settingId,
        int version,
        string settingType,
        string value,
        Guid? groupId = null,
        int? schemaVersion = null,
        CancellationToken ct = default);

    /// <summary><c>DELETE /v1/ui-settings/{settingId}</c>: deletes one setting.</summary>
    Task DeleteSettingAsync(string owner, Guid settingId, CancellationToken ct = default);

    /// <summary><c>DELETE /v1/ui-settings/all</c>: deletes every setting, then every setting group, of the owner.</summary>
    Task EraseSettingsAsync(string owner, CancellationToken ct = default);

    /// <summary><c>POST /v1/ui-setting-groups</c>: creates a setting group for <paramref name="owner"/>.</summary>
    Task<StaticDataSettingGroup> CreateSettingGroupAsync(
        string owner,
        string name,
        string? description = null,
        CancellationToken ct = default);

    /// <summary><c>GET /v1/ui-setting-groups</c>: one page of the owner's setting groups, newest first.</summary>
    Task<StaticDataSettingGroupPage> ListSettingGroupsAsync(
        string owner,
        int pageNumber,
        int pageSize,
        DateTimeOffset? fromDate = null,
        DateTimeOffset? toDate = null,
        CancellationToken ct = default);

    /// <summary><c>GET /v1/ui-setting-groups/{groupId}</c>: one setting group.</summary>
    Task<StaticDataSettingGroup> GetSettingGroupAsync(string owner, Guid groupId, CancellationToken ct = default);

    /// <summary>
    /// <c>PUT /v1/ui-setting-groups/{groupId}</c>: replaces the group's name and description, sending
    /// <paramref name="version"/> as <c>If-Match</c>. Returns the group with its new version.
    /// </summary>
    Task<StaticDataSettingGroup> UpdateSettingGroupAsync(
        string owner,
        Guid groupId,
        int version,
        string name,
        string? description = null,
        CancellationToken ct = default);

    /// <summary><c>DELETE /v1/ui-setting-groups/{groupId}</c>: deletes a setting group with no linked setting.</summary>
    Task DeleteSettingGroupAsync(string owner, Guid groupId, CancellationToken ct = default);
}
