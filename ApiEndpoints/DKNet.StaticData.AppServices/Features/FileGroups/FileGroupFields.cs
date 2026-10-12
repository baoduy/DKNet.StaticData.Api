namespace DKNet.StaticData.AppServices.Features.FileGroups;

/// <summary>
///     The body of a group create or update (spec DRK-2235 §3a). An update replaces all 3 fields, so a field left out
///     or sent as <see langword="null" /> clears it. Every value is kept exactly as sent.
/// </summary>
public sealed record FileGroupFields(string? Name, string? Purpose = null, string? ExternalRef = null)
{
    #region Fields

    private const int MaxNameLength = 100;
    private const int MaxPurposeLength = 100;
    private const int MaxExternalRefLength = 255;

    #endregion

    #region Methods

    /// <summary>Rules R1 and R2: why the fields are refused, or <see langword="null" /> when they may be saved.</summary>
    public string? Refusal()
    {
        if (Name is null || !IsText(Name, MaxNameLength))
        {
            return "The name must be 1 to 100 characters, not only white space, with no control character.";
        }

        if (Purpose is not null && !IsText(Purpose, MaxPurposeLength))
        {
            return "The purpose must be 1 to 100 characters, not only white space, with no control character.";
        }

        return ExternalRef is not null && !IsText(ExternalRef, MaxExternalRefLength)
            ? "The external reference must be 1 to 255 characters, not only white space, with no control character."
            : null;
    }

    private static bool IsText(string value, int maxLength) =>
        value.Length <= maxLength && !string.IsNullOrWhiteSpace(value) && !value.Any(char.IsControl);

    #endregion
}
