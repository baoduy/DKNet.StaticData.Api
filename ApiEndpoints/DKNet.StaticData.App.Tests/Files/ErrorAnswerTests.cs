using DKNet.StaticData.App.Tests.Files.Support;
using DKNet.StaticData.App.TestSupport;

namespace DKNet.StaticData.App.Tests.Files;

/// <summary>
/// Spec DRK-2206 §3: every error answers as problem details (<c>application/problem+json</c> with its status) — a file
/// that does not exist for the owner as much as a refused input or an unknown caller (brief DRK-2209 row 19).
/// </summary>
public sealed class ErrorAnswerTests(FilesHost hosts) : IClassFixture<FilesHost>
{
    public static IEnumerable<object[]> MissingFiles => Databases.With(
        ["read the details of an unknown file"],
        ["download an unknown file"],
        ["delete an unknown file"],
        ["read the details of another owner's file"],
        ["download a deleted file"]);

    [Theory]
    [MemberData(nameof(MissingFiles))]
    public async Task AFileThatDoesNotExistAnswers404AsProblemDetails(TestDatabase database, string call)
    {
        var api = await hosts.OnAsync(database);
        var fileId = await api.HasFileAsync("customer-1", "passport.pdf");
        if (call == "download a deleted file")
        {
            using var deleted = await api.DeleteAsync(Callers.OnboardingSvc, "customer-1", fileId);
            await deleted.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        }

        var unknown = Guid.NewGuid();
        using var response = call switch
        {
            "read the details of an unknown file" => await api.ReadAsync(Callers.OnboardingSvc, "customer-1", unknown),
            "download an unknown file" => await api.DownloadAsync(Callers.OnboardingSvc, "customer-1", unknown),
            "delete an unknown file" => await api.DeleteAsync(Callers.OnboardingSvc, "customer-1", unknown),
            "read the details of another owner's file" => await api.ReadAsync(Callers.OnboardingSvc, "customer-2", fileId),
            "download a deleted file" => await api.DownloadAsync(Callers.OnboardingSvc, "customer-1", fileId),
            _ => throw new ArgumentOutOfRangeException(nameof(call), call, null)
        };

        await response.ShouldBeProblemDetailsAsync(HttpStatusCode.NotFound);
    }

    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task ACallWithoutATokenAnswers401AsProblemDetails(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);

        using var response = await api.ListAsync(token: null, "customer-1");

        await response.ShouldBeProblemDetailsAsync(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.ShouldHaveSingleItem().Scheme.ShouldBe("Bearer");
    }
}
