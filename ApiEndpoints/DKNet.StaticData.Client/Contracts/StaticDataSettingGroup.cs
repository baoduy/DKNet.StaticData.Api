namespace DKNet.StaticData.Client.Contracts;

/// <summary>A setting group, as the setting group create, read, update and list answers carry it. A missing answer field reads as empty.</summary>
public sealed record StaticDataSettingGroup
{
    public Guid GroupId { get; init; }

    /// <summary>The trimmed owner.</summary>
    public string? Owner { get; init; }

    public string? Name { get; init; }

    /// <summary>Empty when the group has no description.</summary>
    public string? Description { get; init; }

    /// <summary>1 at create, up by 1 with each update; send it back to update the group.</summary>
    public int Version { get; init; }

    public string? CreatedBy { get; init; }

    public DateTimeOffset CreatedOn { get; init; }

    /// <summary>Empty until the group's first update.</summary>
    public string? UpdatedBy { get; init; }

    /// <summary>Empty until the group's first update.</summary>
    public DateTimeOffset? UpdatedOn { get; init; }
}

/// <summary>One page of an owner's setting groups, newest first.</summary>
public sealed record StaticDataSettingGroupPage
{
    public IReadOnlyList<StaticDataSettingGroup> Items { get; init; } = [];

    public int PageNumber { get; init; }

    public int PageSize { get; init; }

    public int PageCount { get; init; }

    public long TotalItemCount { get; init; }

    public bool HasNextPage { get; init; }

    public bool HasPreviousPage { get; init; }
}
