namespace DKNet.StaticData.AppServices.Features.SettingGroups;

/// <summary>The details of the setting group answers more than one route gives.</summary>
public static class SettingGroupAnswers
{
    #region Fields

    /// <summary>The same answer for a group that never existed and one of another owner (ADR-0005).</summary>
    public const string NotFound = "The setting group does not exist.";

    public const string NameUsed = "The setting group name is already used by this owner.";

    public const string VersionStale = "The version tag does not name the setting group's current version.";

    #endregion
}
