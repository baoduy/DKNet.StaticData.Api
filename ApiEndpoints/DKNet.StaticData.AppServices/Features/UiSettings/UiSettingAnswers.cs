namespace DKNet.StaticData.AppServices.Features.UiSettings;

/// <summary>The details of the setting answers more than one route gives.</summary>
public static class UiSettingAnswers
{
    #region Fields

    /// <summary>The same answer for a setting that never existed and one of another owner (ADR-0005).</summary>
    public const string NotFound = "The UI setting does not exist.";

    public const string AlreadyExists = "A UI setting with this setting key already exists for this owner.";

    public const string VersionStale = "The version tag does not name the setting's current version.";

    #endregion
}
