using DKNet.StaticData.Api.ApiEndpoints.Files;
using Microsoft.AspNetCore.Http;

namespace DKNet.StaticData.App.Tests.Unit.Files;

/// <summary>
/// Brief DRK-2209 §6a D3 and brief DRK-2247 §6a D1-D3: the owner is exactly one value with no control character as
/// sent, anywhere, the ends included; it is trimmed, and the trimmed owner is 1 to 255 characters.
/// </summary>
public class OwnerQueryTests
{
    public static TheoryData<string, string?> Queries => new()
    {
        { "", null },
        { "?owner=", null },
        { "?owner=%20%20%20", null },
        { "?owner=c", "c" },
        { $"?owner={new string('o', 255)}", new string('o', 255) },
        { $"?owner={new string('o', 256)}", null },
        { "?owner=customer%091", null },
        { "?owner=%20Customer-1%20", "Customer-1" },
        { "?owner=customer-1%20", "customer-1" },
        { $"?owner={new string('o', 255)}%20%20", new string('o', 255) },
        { "?owner=customer-1%09", null },
        { "?owner=%09customer-1", null },
        { "?owner=customer-1&owner=customer-2", null }
    };

    [Theory]
    [MemberData(nameof(Queries))]
    public void AnOwnerIsAcceptedOnlyWhenWellFormed(string query, string? expected)
    {
        var context = new DefaultHttpContext { Request = { QueryString = new QueryString(query) } };

        var accepted = OwnerQuery.TryRead(context.Request, out var owner);

        accepted.ShouldBe(expected is not null);
        if (expected is not null)
        {
            owner.ShouldBe(expected);
        }
    }
}
