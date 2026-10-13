namespace DKNet.StaticData.AppServices.Features.FileGroups;

/// <summary>
///     The body of a group create or update (spec DRK-2235 §3a). An update replaces all 3 fields, so a field left out
///     or sent as <see langword="null" /> clears it. Every value is stored trimmed (spec DRK-2245 §3).
/// </summary>
public sealed record FileGroupFields(string? Name, string? Purpose = null, string? ExternalRef = null);

/// <summary>
///     Rules R1 and R2 under the text rules of spec DRK-2245 §3: the name is a required display text of 1 to 100
///     characters, the purpose an optional caller text of up to 100 and the external reference one of up to 255.
/// </summary>
internal sealed class FileGroupFieldsValidator : AbstractValidator<FileGroupFields>
{
    #region Constructors

    public FileGroupFieldsValidator()
    {
        RuleFor(f => f.Name).DisplayText(maxLength: 100, required: true);
        RuleFor(f => f.Purpose).CallerText(maxLength: 100, required: false);
        RuleFor(f => f.ExternalRef).CallerText(maxLength: 255, required: false);
    }

    #endregion
}
