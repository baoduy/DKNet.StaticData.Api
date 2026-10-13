namespace DKNet.StaticData.Client.Contracts;

/// <summary>A file group, as the group create, read, update and list answers carry it. A missing answer field reads as empty.</summary>
public sealed record StaticDataFileGroup
{
    public Guid GroupId { get; init; }

    /// <summary>The owner, as sent at create.</summary>
    public string? Owner { get; init; }

    public string? Name { get; init; }

    /// <summary>Empty when the group has no purpose.</summary>
    public string? Purpose { get; init; }

    /// <summary>Empty when the group has no external reference.</summary>
    public string? ExternalRef { get; init; }

    /// <summary>1 at create, up by 1 with each update; send it back to update the group.</summary>
    public int Version { get; init; }

    public string? CreatedBy { get; init; }

    public DateTimeOffset CreatedOn { get; init; }

    /// <summary>Empty until the group's first update.</summary>
    public string? UpdatedBy { get; init; }

    /// <summary>Empty until the group's first update.</summary>
    public DateTimeOffset? UpdatedOn { get; init; }
}

/// <summary>One page of an owner's file groups, newest first.</summary>
public sealed record StaticDataFileGroupPage
{
    public IReadOnlyList<StaticDataFileGroup> Items { get; init; } = [];

    public int PageNumber { get; init; }

    public int PageSize { get; init; }

    public int PageCount { get; init; }

    public long TotalItemCount { get; init; }

    public bool HasNextPage { get; init; }

    public bool HasPreviousPage { get; init; }
}
