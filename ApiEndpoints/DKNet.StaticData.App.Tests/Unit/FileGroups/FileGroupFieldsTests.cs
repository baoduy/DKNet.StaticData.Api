using DKNet.StaticData.AppServices.Features.FileGroups;

namespace DKNet.StaticData.App.Tests.Unit.FileGroups;

/// <summary>Spec DRK-2235 §6 R1 and R2 at their edges: the lengths, white space, control characters and none.</summary>
public class FileGroupFieldsTests
{
    private const string NameRule = "The name must be 1 to 100 characters, not only white space, with no control character.";
    private const string PurposeRule = "The purpose must be 1 to 100 characters, not only white space, with no control character.";

    private const string ExternalRefRule =
        "The external reference must be 1 to 255 characters, not only white space, with no control character.";

    public static TheoryData<string?, string?, string?, string?> Fields => new()
    {
        { "N", null, null, null },
        { new string('n', 100), new string('p', 100), new string('e', 255), null },
        { " Pack ", " p ", " e ", null },
        { null, null, null, NameRule },
        { "", null, null, NameRule },
        { "   ", null, null, NameRule },
        { new string('n', 101), null, null, NameRule },
        { "Pack\u0001", null, null, NameRule },
        { "Pack", "", null, PurposeRule },
        { "Pack", "  ", null, PurposeRule },
        { "Pack", new string('p', 101), null, PurposeRule },
        { "Pack", "a\tb", null, PurposeRule },
        { "Pack", null, "", ExternalRefRule },
        { "Pack", null, "  ", ExternalRefRule },
        { "Pack", null, new string('e', 256), ExternalRefRule },
        { "Pack", null, "a\nb", ExternalRefRule }
    };

    [Theory]
    [MemberData(nameof(Fields))]
    public void TheFieldsAreRefusedOnlyByTheirRule(string? name, string? purpose, string? externalRef, string? refusal) =>
        new FileGroupFields(name, purpose, externalRef).Refusal().ShouldBe(refusal);
}
