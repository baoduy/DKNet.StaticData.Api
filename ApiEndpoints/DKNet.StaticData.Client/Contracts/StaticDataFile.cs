namespace DKNet.StaticData.Client.Contracts;

/// <summary>A file's details, as the upload, read and list answers carry them. A missing answer field reads as empty.</summary>
public sealed record StaticDataFile
{
    public Guid FileId { get; init; }

    public string? Owner { get; init; }

    public string? FileName { get; init; }

    public string? ContentType { get; init; }

    public long SizeBytes { get; init; }

    /// <summary>SHA-256 of the stored bytes, 64 lowercase hex characters.</summary>
    public string? Checksum { get; init; }

    public int Version { get; init; }

    public string? CreatedBy { get; init; }

    public DateTimeOffset CreatedOn { get; init; }

    /// <summary>Empty until the file's first update.</summary>
    public string? UpdatedBy { get; init; }

    /// <summary>Empty until the file's first update.</summary>
    public DateTimeOffset? UpdatedOn { get; init; }
}

/// <summary>One page of an owner's files, newest first.</summary>
public sealed record StaticDataFilePage
{
    public IReadOnlyList<StaticDataFile> Items { get; init; } = [];

    public int PageNumber { get; init; }

    public int PageSize { get; init; }

    public int PageCount { get; init; }

    public long TotalItemCount { get; init; }

    public bool HasNextPage { get; init; }

    public bool HasPreviousPage { get; init; }
}

/// <summary>An RFC 9457 problem details body, as the service sends it with every error answer.</summary>
public sealed record StaticDataProblemDetails
{
    public string? Type { get; init; }

    public string? Title { get; init; }

    public int? Status { get; init; }

    public string? Detail { get; init; }

    public string? Instance { get; init; }
}
