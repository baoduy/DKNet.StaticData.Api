using DKNet.StaticData.AppServices.Share;
using FluentValidation;

namespace DKNet.StaticData.App.Tests.Unit.Share;

/// <summary>
/// Brief DRK-2247 §6a D5 and D6, and spec DRK-2245 §3 "Text rules on every /v1 route": the control character check on
/// the text as sent, the trim, the length on the trimmed text, a required text empty after trimming, an optional text of
/// only spaces, and the display text characters.
/// </summary>
public class TextRulesTests
{
    private sealed record Probe(string? Text);

    public static TheoryData<string?, string?> Trims => new()
    {
        { null, null },
        { "customer-1", "customer-1" },
        { " Customer screens ", "Customer screens" },
        { "  passport.pdf", "passport.pdf" },
        { "   ", "" }
    };

    [Theory]
    [MemberData(nameof(Trims))]
    public void TrimRemovesTheWhiteSpaceAtBothEnds(string? text, string? trimmed) =>
        AppServices.Share.TextRules.Trim(text).ShouldBe(trimmed);

    public static TheoryData<string?, bool, bool> CallerTexts => new()
    {
        // text, required, accepted
        { "customer-1", true, true },
        { " customer-1 ", true, true },
        { "Café/2 v.", true, true },
        { new string('c', 10) + "  ", true, true },
        { new string('c', 11), true, false },
        { "customer-1\t", true, false },
        { "\tcustomer-1", true, false },
        { "customer\n1", true, false },
        { "   ", true, false },
        { "", true, false },
        { null, true, false },
        { null, false, true },
        { " x ", false, true },
        { "  ", false, false },
        { "", false, false }
    };

    [Theory]
    [MemberData(nameof(CallerTexts))]
    public void ACallerTextIsAcceptedOnlyByItsRule(string? text, bool required, bool accepted)
    {
        var validator = new InlineValidator<Probe>();
        validator.RuleFor(p => p.Text).CallerText(maxLength: 10, required);

        validator.Validate(new Probe(text)).IsValid.ShouldBe(accepted);
    }

    public static TheoryData<string?, bool, bool> DisplayTexts => new()
    {
        // text, required, accepted
        { "Onboarding pack 2026", true, true },
        { "Pack-2", true, true },
        { "my_layouts", true, true },
        { " Customer screens ", true, true },
        { new string('n', 20) + "  ", true, true },
        { new string('n', 21), true, false },
        { "Café", true, false },
        { "Pack, v2.", true, false },
        { "Pack/2", true, false },
        { "Pack\t", true, false },
        { "   ", true, false },
        { null, true, false },
        { null, false, true },
        { "Main screens", false, true },
        { "Main screens.", false, false },
        { "", false, false },
        { "   ", false, false }
    };

    [Theory]
    [MemberData(nameof(DisplayTexts))]
    public void ADisplayTextIsAcceptedOnlyByItsRule(string? text, bool required, bool accepted)
    {
        var validator = new InlineValidator<Probe>();
        validator.RuleFor(p => p.Text).DisplayText(maxLength: 20, required);

        validator.Validate(new Probe(text)).IsValid.ShouldBe(accepted);
    }
}
