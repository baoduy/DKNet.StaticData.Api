using DKNet.StaticData.App.Tests.Files.Support;
using DKNet.StaticData.App.TestSupport;

namespace DKNet.StaticData.App.Tests.Files;

/// <summary>
/// Spec DRK-2206 §5 "Owner". Every <c>/v1</c> route needs the <c>owner</c> query parameter — 1 to 255 characters, not
/// only white space, no control characters — kept exactly as sent; a file of another owner does not exist for the call.
/// </summary>
public sealed class OwnerTests(FilesHost hosts) : IClassFixture<FilesHost>
{
    public static IEnumerable<object[]> BadOwners => Databases.With(
        ["no owner"],
        ["the owner \"   \""],
        ["an owner of 256 characters"],
        ["an owner with a tab character"]);

    /// <summary>Scenario Outline: A missing or malformed owner is refused.</summary>
    [Theory]
    [MemberData(nameof(BadOwners))]
    public async Task AMissingOrMalformedOwnerIsRefused(TestDatabase database, string owner)
    {
        var api = await hosts.OnAsync(database);
        var sent = owner switch
        {
            "no owner" => null,
            "the owner \"   \"" => "   ",
            "an owner of 256 characters" => new string('o', 256),
            "an owner with a tab character" => "customer\t1",
            _ => throw new ArgumentOutOfRangeException(nameof(owner), owner, null)
        };

        using var response = await api.ListAsync(Callers.OnboardingSvc, sent);

        await response.ShouldBeProblemDetailsAsync(HttpStatusCode.BadRequest);
    }

    /// <summary>Scenario Outline: Owners compare exactly on each database.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task OwnersCompareExactlyOnEachDatabase(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        await api.HasFileAsync("Customer-1", "passport.pdf");

        (await api.FileNamesAsync("customer-1")).ShouldBeEmpty();
        // presence sibling: the same list call does find the file under the owner exactly as sent
        (await api.FileNamesAsync("Customer-1")).ShouldBe(["passport.pdf"]);
    }

    public static IEnumerable<object[]> OtherOwnerActions => Databases.With(
        ["read the details"],
        ["download"],
        ["delete"]);

    /// <summary>Scenario Outline: A file of another owner does not exist for the caller.</summary>
    [Theory]
    [MemberData(nameof(OtherOwnerActions))]
    public async Task AFileOfAnotherOwnerDoesNotExistForTheCaller(TestDatabase database, string action)
    {
        var api = await hosts.OnAsync(database);
        var fileId = await api.HasFileAsync("customer-1", "passport.pdf");

        using var response = action switch
        {
            "read the details" => await api.ReadAsync(Callers.OnboardingSvc, "customer-2", fileId),
            "download" => await api.DownloadAsync(Callers.OnboardingSvc, "customer-2", fileId),
            "delete" => await api.DeleteAsync(Callers.OnboardingSvc, "customer-2", fileId),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        };

        await response.ShouldHaveStatusAsync(HttpStatusCode.NotFound);
        (await api.FileNamesAsync("customer-1")).ShouldBe(["passport.pdf"]);
    }
}
