namespace DKNet.StaticData.Share.Options;

/// <summary>
///     The operator's file settings, bound from the <see cref="Name" /> section (design <c>04-data.md</c>, FileSettings).
/// </summary>
public sealed class FileSettings
{
    #region Properties

    /// <summary>The configuration section the settings bind from.</summary>
    public static string Name => "Files";

    /// <summary>The file name extensions a stored file may carry, each with its dot.</summary>
    public IReadOnlyList<string> AllowedExtensions { get; set; } = [];

    #endregion
}
