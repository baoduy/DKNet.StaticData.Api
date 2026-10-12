using DKNet.EfCore.DataAuthorization;
using DKNet.StaticData.Domains.Share;

namespace DKNet.StaticData.Domains.Features.FileGroups.Entities;

/// <summary>
///     A named set of one owner's files (spec DRK-2235 §3a). Files link to it by its id; the owner is stamped on save
///     by DKNet's data-owner hook from the call's owner.
/// </summary>
public sealed class FileGroup : AggregateRoot, IOwnedBy
{
    #region Constructors

    private FileGroup(Guid id, string name, string? purpose, string? externalRef, string createdBy)
        : base(id, createdBy)
    {
        Name = name;
        Purpose = purpose;
        ExternalRef = externalRef;
    }

    /// <summary>For EF Core materialisation only.</summary>
    private FileGroup()
    {
    }

    #endregion

    #region Properties

    /// <summary>The caller's own reference for the group, such as a customer number. Compared exactly.</summary>
    public string? ExternalRef { get; private set; }

    /// <summary>The name, kept exactly as sent.</summary>
    public string Name { get; private set; } = string.Empty;

    public string OwnedBy { get; private set; } = string.Empty;

    /// <summary>What the group is for, such as <c>customer-onboarding</c>. Compared exactly.</summary>
    public string? Purpose { get; private set; }

    /// <summary>The app-managed version (ADR-0010), 1 at create; the concurrency token.</summary>
    public int Version { get; private set; } = 1;

    #endregion

    #region Methods

    /// <summary>A new group with a new version-7 id and version 1.</summary>
    /// <param name="name">The name, already checked.</param>
    /// <param name="purpose">The purpose, or <see langword="null" /> for none.</param>
    /// <param name="externalRef">The external reference, or <see langword="null" /> for none.</param>
    /// <param name="createdBy">The caller id.</param>
    public static FileGroup Create(string name, string? purpose, string? externalRef, string createdBy) =>
        new(Guid.CreateVersion7(), name, purpose, externalRef, createdBy);

    /// <summary>Replaces all 3 fields and raises the version by 1.</summary>
    /// <param name="name">The name, already checked.</param>
    /// <param name="purpose">The purpose, or <see langword="null" /> for none.</param>
    /// <param name="externalRef">The external reference, or <see langword="null" /> for none.</param>
    /// <param name="updatedBy">The caller id.</param>
    public void Update(string name, string? purpose, string? externalRef, string updatedBy)
    {
        Name = name;
        Purpose = purpose;
        ExternalRef = externalRef;
        Version++;
        SetUpdatedBy(updatedBy);
    }

    #endregion
}
