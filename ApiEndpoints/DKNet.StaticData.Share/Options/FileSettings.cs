namespace DKNet.StaticData.Share.Options;

/// <summary>
///     The operator's file settings, bound from the <see cref="Name" /> section (design <c>04-data.md</c>, FileSettings).
/// </summary>
public sealed class FileSettings
{
    #region Properties

    /// <summary>The configuration section the settings bind from.</summary>
    public static string Name => "Files";

    /// <summary>The largest file the service stores, in bytes: the requester's 50 MB. Not a setting.</summary>
    public static long MaxFileSizeBytes => 50_000_000;

    /// <summary>The allow-list used when the operator sets none: the design's 14 extensions.</summary>
    public static IReadOnlyList<string> DefaultAllowedExtensions { get; } =
    [
        ".pdf", ".png", ".jpg", ".jpeg", ".gif", ".txt", ".csv", ".json", ".xml", ".doc", ".docx", ".xls", ".xlsx", ".zip"
    ];

    /// <summary>The file name extensions a stored file may carry, each with its dot, in lowercase.</summary>
    public IReadOnlyList<string> AllowedExtensions { get; set; } = [];

    #endregion
}
