using DKNet.EfCore.DataAuthorization;
using DKNet.StaticData.Domains.Share;

namespace DKNet.StaticData.Domains.Features.UiSettings.Entities;

/// <summary>
///     One display setting of one owner, such as a screen's table layout (spec DRK-2245 §3a). At most one exists per
///     owner and setting key; the owner is stamped on save by DKNet's data-owner hook from the call's owner.
/// </summary>
public sealed class UiSetting : AggregateRoot, IOwnedBy
{
    #region Constructors

    private UiSetting(
        Guid id,
        string settingKey,
        string settingType,
        Guid? groupId,
        string value,
        int schemaVersion,
        string createdBy)
        : base(id, createdBy)
    {
        SettingKey = settingKey;
        SettingType = settingType;
        GroupId = groupId;
        Value = value;
        SchemaVersion = schemaVersion;
    }

    /// <summary>For EF Core materialisation only.</summary>
    private UiSetting()
    {
    }

    #endregion

    #region Properties

    /// <summary>The setting group of the same owner, or <see langword="null" /> for none.</summary>
    public Guid? GroupId { get; private set; }

    public string OwnedBy { get; private set; } = string.Empty;

    /// <summary>The version of the value's own shape, which the caller sets; 1 when it sends none.</summary>
    public int SchemaVersion { get; private set; } = 1;

    /// <summary>The key, such as <c>customers.list.table</c>. Compared exactly; it never changes.</summary>
    public string SettingKey { get; private set; } = string.Empty;

    /// <summary>The caller's free-text type of the value, such as <c>TableLayout</c>.</summary>
    public string SettingType { get; private set; } = string.Empty;

    /// <summary>The value: JSON text kept exactly as sent, never read by the service.</summary>
    public string Value { get; private set; } = string.Empty;

    /// <summary>The app-managed version (ADR-0010), 1 at create; the concurrency token.</summary>
    public int Version { get; private set; } = 1;

    #endregion

    #region Methods

    /// <summary>A new setting with a new version-7 id and version 1.</summary>
    /// <param name="settingKey">The key, already checked and trimmed.</param>
    /// <param name="settingType">The type, already checked and trimmed.</param>
    /// <param name="groupId">The group, or <see langword="null" /> for none.</param>
    /// <param name="value">The value as sent.</param>
    /// <param name="schemaVersion">The value's schema version.</param>
    /// <param name="createdBy">The caller id.</param>
    public static UiSetting Create(
        string settingKey,
        string settingType,
        Guid? groupId,
        string value,
        int schemaVersion,
        string createdBy) =>
        new(Guid.CreateVersion7(), settingKey, settingType, groupId, value, schemaVersion, createdBy);

    /// <summary>
    ///     Replaces every field but the key and raises the version by 1, even when nothing changed.
    /// </summary>
    /// <param name="settingType">The type, already checked and trimmed.</param>
    /// <param name="groupId">The group, or <see langword="null" /> for none.</param>
    /// <param name="value">The value as sent.</param>
    /// <param name="schemaVersion">The value's schema version.</param>
    /// <param name="updatedBy">The caller id.</param>
    public void Update(string settingType, Guid? groupId, string value, int schemaVersion, string updatedBy)
    {
        SettingType = settingType;
        GroupId = groupId;
        Value = value;
        SchemaVersion = schemaVersion;
        Version++;
        SetUpdatedBy(updatedBy);
    }

    #endregion
}
