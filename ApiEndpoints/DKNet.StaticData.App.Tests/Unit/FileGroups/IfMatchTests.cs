using DKNet.StaticData.Api.ApiEndpoints;
using Microsoft.AspNetCore.Http;

namespace DKNet.StaticData.App.Tests.Unit.FileGroups;

/// <summary>
/// Brief DRK-2239 §6a D1-D4: a missing <c>If-Match</c> is told apart from a sent one; only exactly one strong tag
/// <c>"n"</c> names a version, anything else names none.
/// </summary>
public class IfMatchTests
{
    public static TheoryData<string[], int?> Headers => new()
    {
        { [], null },
        { ["\"2\""], 2 },
        { ["\"1\""], 1 },
        { ["*"], 0 },
        { ["W/\"2\""], 0 },
        { ["2"], 0 },
        { ["\"a\""], 0 },
        { ["\"\""], 0 },
        { ["\"-1\""], 0 },
        { ["\"1\", \"2\""], 0 },
        { ["\"1\"", "\"2\""], 0 }
    };

    [Theory]
    [MemberData(nameof(Headers))]
    public void OnlyOneStrongTagNamesAVersion(string[] values, int? expected)
    {
        var context = new DefaultHttpContext();
        if (values.Length > 0)
        {
            context.Request.Headers.IfMatch = values;
        }

        IfMatch.Read(context.Request).ShouldBe(expected);
    }
}
