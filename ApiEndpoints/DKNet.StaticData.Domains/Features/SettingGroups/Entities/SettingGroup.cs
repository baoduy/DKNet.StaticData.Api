using DKNet.EfCore.DataAuthorization;
using DKNet.StaticData.Domains.Share;

namespace DKNet.StaticData.Domains.Features.SettingGroups.Entities;

/// <summary>
///     A named set of one owner's UI settings (spec DRK-2245 §3a). Settings link to it by its id; the owner is stamped
///     on save by DKNet's data-owner hook from the call's owner.
/// </summary>
public sealed class SettingGroup : AggregateRoot, IOwnedBy
{
    #region Constructors

    private SettingGroup(Guid id, string name, string? description, string createdBy)
        : base(id, createdBy)
    {
        Name = name;
        Description = description;
    }

    /// <summary>For EF Core materialisation only.</summary>
    private SettingGroup()
    {
    }

    #endregion

    #region Properties

    /// <summary>What the group holds, or <see langword="null" /> for none.</summary>
    public string? Description { get; private set; }

    /// <summary>The name, unique per owner and compared exactly.</summary>
    public string Name { get; private set; } = string.Empty;

    public string OwnedBy { get; private set; } = string.Empty;

    /// <summary>The app-managed version (ADR-0010), 1 at create; the concurrency token.</summary>
    public int Version { get; private set; } = 1;

    #endregion

    #region Methods

    /// <summary>A new group with a new version-7 id and version 1.</summary>
    /// <param name="name">The name, already checked and trimmed.</param>
    /// <param name="description">The description, or <see langword="null" /> for none.</param>
    /// <param name="createdBy">The caller id.</param>
    public static SettingGroup Create(string name, string? description, string createdBy) =>
        new(Guid.CreateVersion7(), name, description, createdBy);

    /// <summary>Replaces the name and the description and raises the version by 1.</summary>
    /// <param name="name">The name, already checked and trimmed.</param>
    /// <param name="description">The description, or <see langword="null" /> for none.</param>
    /// <param name="updatedBy">The caller id.</param>
    public void Update(string name, string? description, string updatedBy)
    {
        Name = name;
        Description = description;
        Version++;
        SetUpdatedBy(updatedBy);
    }

    #endregion
}
