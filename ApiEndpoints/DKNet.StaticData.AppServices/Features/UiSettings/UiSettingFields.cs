using System.Text;

namespace DKNet.StaticData.AppServices.Features.UiSettings;

/// <summary>
///     The body of a setting update (spec DRK-2245 §3a): it replaces the type, the group, the value and the schema
///     version, so a group left out or sent as <see langword="null" /> unlinks the setting and a schema version left out
///     becomes 1. A setting key in it is ignored.
/// </summary>
public record UiSettingFields
{
    #region Properties

    public string? SettingType { get; init; }

    public Guid? GroupId { get; init; }

    /// <summary>The value's JSON text exactly as sent; <see langword="null" /> when it is left out or sent as <c>null</c>.</summary>
    [JsonConverter(typeof(RawJsonConverter))]
    public string? Value { get; init; }

    public int? SchemaVersion { get; init; }

    #endregion
}

/// <summary>The body of a setting create: the fields of an update plus the setting key, which never changes after.</summary>
public sealed record NewUiSettingFields : UiSettingFields
{
    #region Properties

    public string? SettingKey { get; init; }

    #endregion
}

/// <summary>The setting fields of spec DRK-2245 §3 "UI setting fields", on update.</summary>
internal sealed class UiSettingFieldsValidator : AbstractValidator<UiSettingFields>
{
    #region Fields

    /// <summary>The most UTF-8 bytes a value's text may take, as sent.</summary>
    internal const int MaxValueBytes = 65_536;

    #endregion

    #region Constructors

    public UiSettingFieldsValidator()
    {
        RuleFor(f => f.SettingType).CallerText(maxLength: 50, required: true);
        RuleFor(f => f.Value)
            .NotNull().WithMessage("The value is required: any JSON except null.")
            .Must(v => v is null || Encoding.UTF8.GetByteCount(v) <= MaxValueBytes)
            .WithMessage("The value must be at most 65,536 bytes as UTF-8.");
        RuleFor(f => f.SchemaVersion).GreaterThanOrEqualTo(1);
    }

    #endregion
}

/// <summary>The setting fields on create: those of an update, and a setting key.</summary>
internal sealed class NewUiSettingFieldsValidator : AbstractValidator<NewUiSettingFields>
{
    #region Constructors

    public NewUiSettingFieldsValidator()
    {
        RuleFor(f => f.SettingKey).CallerText(maxLength: 200, required: true);
        Include(new UiSettingFieldsValidator());
    }

    #endregion
}
