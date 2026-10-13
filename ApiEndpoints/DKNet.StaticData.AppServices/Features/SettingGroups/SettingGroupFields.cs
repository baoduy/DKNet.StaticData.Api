namespace DKNet.StaticData.AppServices.Features.SettingGroups;

/// <summary>
///     The body of a setting group create or update (spec DRK-2245 §3a). An update replaces both fields, so a
///     description left out or sent as <see langword="null" /> clears it.
/// </summary>
public sealed record SettingGroupFields(string? Name, string? Description = null);

/// <summary>The name and the description are display texts (spec DRK-2245 §3, Text rules).</summary>
internal sealed class SettingGroupFieldsValidator : AbstractValidator<SettingGroupFields>
{
    #region Constructors

    public SettingGroupFieldsValidator()
    {
        RuleFor(f => f.Name).DisplayText(maxLength: 100, required: true);
        RuleFor(f => f.Description).DisplayText(maxLength: 500, required: false);
    }

    #endregion
}
