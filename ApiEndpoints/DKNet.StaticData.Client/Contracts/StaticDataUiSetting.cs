using System.Text.Json.Serialization;

namespace DKNet.StaticData.Client.Contracts;

/// <summary>A UI setting, as the setting create, read, update and list answers carry it. A missing answer field reads as empty.</summary>
public sealed record StaticDataUiSetting
{
    public Guid SettingId { get; init; }

    /// <summary>The trimmed owner.</summary>
    public string? Owner { get; init; }

    public string? SettingKey { get; init; }

    public string? SettingType { get; init; }

    /// <summary>Empty when the setting has no group.</summary>
    public Guid? GroupId { get; init; }

    /// <summary>The value as raw JSON text, exactly as the service answered it.</summary>
    [JsonConverter(typeof(RawJsonConverter))]
    public string? Value { get; init; }

    public int SchemaVersion { get; init; }

    /// <summary>1 at create, up by 1 with each update; send it back to update the setting.</summary>
    public int Version { get; init; }

    public string? CreatedBy { get; init; }

    public DateTimeOffset CreatedOn { get; init; }

    /// <summary>Empty until the setting's first update.</summary>
    public string? UpdatedBy { get; init; }

    /// <summary>Empty until the setting's first update.</summary>
    public DateTimeOffset? UpdatedOn { get; init; }
}

/// <summary>One page of an owner's UI settings, newest first.</summary>
public sealed record StaticDataUiSettingPage
{
    public IReadOnlyList<StaticDataUiSetting> Items { get; init; } = [];

    public int PageNumber { get; init; }

    public int PageSize { get; init; }

    public int PageCount { get; init; }

    public long TotalItemCount { get; init; }

    public bool HasNextPage { get; init; }

    public bool HasPreviousPage { get; init; }
}
