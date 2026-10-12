namespace DKNet.StaticData.AppServices.Features.FileGroups;

/// <summary>The details of the group answers more than one route gives.</summary>
public static class FileGroupAnswers
{
    #region Fields

    /// <summary>The same answer for a group that never existed and one of another owner (ADR-0005).</summary>
    public const string NotFound = "The file group does not exist.";

    public const string VersionStale = "The version tag does not name the group's current version.";

    #endregion
}
