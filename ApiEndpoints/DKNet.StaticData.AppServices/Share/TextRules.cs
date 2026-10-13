namespace DKNet.StaticData.AppServices.Share;

/// <summary>
///     The text rules every <c>/v1</c> route applies to a text a caller sends (spec DRK-2245 §3, design ADR-0018 and
///     ADR-0019): a control character anywhere in the text as sent is refused; then the white space at both ends is
///     trimmed; the length counts the trimmed text; a required text that is empty after trimming, and an optional text
///     that holds only spaces, are refused. A display text also allows only letters a-z and A-Z, digits 0-9, spaces,
///     <c>-</c> and <c>_</c>.
/// </summary>
public static class TextRules
{
    #region Methods

    /// <summary>The text with the white space at both ends removed; <see langword="null" /> stays <see langword="null" />.</summary>
    public static string? Trim(string? text) => text?.Trim();

    /// <summary>
    ///     A caller text: no control character as sent, at most <paramref name="maxLength" /> characters after trimming,
    ///     and, when <paramref name="required" />, not empty after trimming.
    /// </summary>
    public static IRuleBuilderOptions<T, string?> CallerText<T>(
        this IRuleBuilder<T, string?> rule,
        int maxLength,
        bool required) =>
        rule.Must(text => text is null || !text.Any(char.IsControl))
            .WithMessage("'{PropertyName}' must have no control character.")
            .Must(text => text is null ? !required : text.Trim().Length is var length && length > 0 && length <= maxLength)
            .WithMessage($"'{{PropertyName}}' must be 1 to {maxLength} characters, not counting the spaces at both ends.");

    /// <summary>
    ///     A display text: a caller text (<see cref="CallerText{T}" />) that, after trimming, holds only letters a-z and
    ///     A-Z, digits 0-9, spaces, <c>-</c> and <c>_</c>.
    /// </summary>
    public static IRuleBuilderOptions<T, string?> DisplayText<T>(
        this IRuleBuilder<T, string?> rule,
        int maxLength,
        bool required) =>
        rule.CallerText(maxLength, required)
            .Must(text => text is null || text.Trim().All(c => char.IsAsciiLetterOrDigit(c) || c is ' ' or '-' or '_'))
            .WithMessage("'{PropertyName}' may hold only letters a-z and A-Z, digits 0-9, spaces, '-' and '_'.");

    #endregion
}
