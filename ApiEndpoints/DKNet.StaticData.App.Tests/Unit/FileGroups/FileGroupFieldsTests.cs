using DKNet.StaticData.AppServices.Features.FileGroups;
using FluentValidation;

namespace DKNet.StaticData.App.Tests.Unit.FileGroups;

/// <summary>
/// Spec DRK-2235 §6 R1 and R2 at their edges, under the text rules of spec DRK-2245 §3: each field is trimmed before
/// its length counts, a control character as sent is refused, the name is a display text (letters, digits, spaces,
/// <c>-</c> and <c>_</c>) and the purpose and external reference are caller texts. The fields are checked by the
/// service's FluentValidation validator of <see cref="FileGroupFields"/>, found by its type, not its name.
/// </summary>
public class FileGroupFieldsTests
{
    public static TheoryData<string?, string?, string?, string?> Fields => new()
    {
        // name, purpose, external reference, the one field refused (null: none)
        { "N", null, null, null },
        { new string('n', 100), new string('p', 100), new string('e', 255), null },
        { " Pack ", " p ", " e ", null },
        { new string('n', 100) + "  ", " " + new string('p', 100), new string('e', 255) + " ", null },
        { "Pack", "Café/2", "CUS-0042, v2.", null },
        { null, null, null, "Name" },
        { "", null, null, "Name" },
        { "   ", null, null, "Name" },
        { new string('n', 101), null, null, "Name" },
        { "Pack\u0001", null, null, "Name" },
        { "Pack\t", null, null, "Name" },
        { "Café", null, null, "Name" },
        { "Pack/2", null, null, "Name" },
        { "Pack, v2.", null, null, "Name" },
        { "Pack", "", null, "Purpose" },
        { "Pack", "  ", null, "Purpose" },
        { "Pack", new string('p', 101), null, "Purpose" },
        { "Pack", "a\tb", null, "Purpose" },
        { "Pack", "customer-onboarding\t", null, "Purpose" },
        { "Pack", null, "", "ExternalRef" },
        { "Pack", null, "  ", "ExternalRef" },
        { "Pack", null, new string('e', 256), "ExternalRef" },
        { "Pack", null, "a\nb", "ExternalRef" },
        { "Pack", null, "\tCUS-0042", "ExternalRef" }
    };

    [Theory]
    [MemberData(nameof(Fields))]
    public void TheFieldsAreRefusedOnlyByTheirRule(string? name, string? purpose, string? externalRef, string? refused)
    {
        var result = Validator().Validate(new FileGroupFields(name, purpose, externalRef));

        result.Errors.Select(e => e.PropertyName).Distinct().ShouldBe(refused is null ? [] : [refused]);
    }

    /// <summary>The service's one validator of <see cref="FileGroupFields"/>, whatever its class is named.</summary>
    private static IValidator<FileGroupFields> Validator()
    {
        var type = typeof(FileGroupFields).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IValidator<FileGroupFields>).IsAssignableFrom(t))
            .ShouldHaveSingleItem("the service has no FluentValidation validator of FileGroupFields");
        return (IValidator<FileGroupFields>)Activator.CreateInstance(type)!;
    }
}
