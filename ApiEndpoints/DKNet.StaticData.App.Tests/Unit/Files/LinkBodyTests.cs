using System.Text;
using DKNet.StaticData.Api.ApiEndpoints.Files;

namespace DKNet.StaticData.App.Tests.Unit.Files;

/// <summary>
/// Brief DRK-2237 §6a D6-D8: a link body holds a <c>groupId</c> field that is a group id, or <see langword="null" /> to
/// unlink; a missing field is told apart from <see langword="null" />, and any other body is refused, never thrown.
/// </summary>
public class LinkBodyTests
{
    public static TheoryData<string> RefusedBodies => new()
    {
        "",
        "   ",
        "{}",
        "not json",
        "{\"groupId\":",
        "{\"groupId\":\"not-a-guid\"}",
        "{\"groupId\":\"\"}",
        "{\"groupId\":42}",
        "{\"groupId\":true}",
        "{\"groupId\":{}}",
        "{\"groupId\":[\"0199d3a4-7c1e-7d2a-9b3c-4d5e6f708192\"]}",
        "{\"GroupId\":\"0199d3a4-7c1e-7d2a-9b3c-4d5e6f708192\"}",
        "[{\"groupId\":\"0199d3a4-7c1e-7d2a-9b3c-4d5e6f708192\"}]",
        "\"0199d3a4-7c1e-7d2a-9b3c-4d5e6f708192\"",
        "null"
    };

    [Theory]
    [MemberData(nameof(RefusedBodies))]
    public async Task ABodyWithoutAGroupIdOrNullIsRefused(string body)
    {
        (await ReadAsync(body)).ShouldBe((false, null));
    }

    [Fact]
    public async Task AGroupIdLinks()
    {
        (await ReadAsync("{\"groupId\":\"0199d3a4-7c1e-7d2a-9b3c-4d5e6f708192\"}"))
            .ShouldBe((true, Guid.Parse("0199d3a4-7c1e-7d2a-9b3c-4d5e6f708192")));
    }

    [Fact]
    public async Task NullUnlinks()
    {
        (await ReadAsync("{\"groupId\":null}")).ShouldBe((true, null));
    }

    private static async Task<(bool IsValid, Guid? GroupId)> ReadAsync(string body)
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(body));
        return await LinkBody.ReadAsync(stream, CancellationToken.None);
    }
}
