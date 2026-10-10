using DKNet.StaticData.App.Tests.Files.Support;
using DKNet.StaticData.App.TestSupport;
using Microsoft.Extensions.Logging;

namespace DKNet.StaticData.App.Tests.Files;

/// <summary>
/// Brief DRK-2209 §6a rows the spec's scenarios leave open: D5 (file names) and D9 (a provider that reports missing
/// bytes by throwing <see cref="FileNotFoundException" />, as the cloud providers do, rather than returning none).
/// </summary>
public sealed class FileEdgeCaseTests(FilesHost hosts) : IClassFixture<FilesHost>
{
    public static IEnumerable<object[]> StoredNames => Databases.With(
        ["a/b/passport.pdf", "passport.pdf", "application/pdf"],
        [".PDF", ".PDF", "application/pdf"],
        [new string('n', 251) + ".pdf", new string('n', 251) + ".pdf", "application/pdf"],
        ["notes.TXT", "notes.TXT", "text/plain"]);

    [Theory]
    [MemberData(nameof(StoredNames))]
    public async Task AFileIsStoredUnderItsNameWithoutThePath(TestDatabase database, string sent, string stored, string contentType)
    {
        var api = await hosts.OnAsync(database);

        using var response = await api.UploadAsync(Callers.OnboardingSvc, "customer-1", sent, Bytes.Of(10), FilesApi.NewKey());

        await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
        var details = await response.JsonAsync();
        details.GetProperty("fileName").GetString().ShouldBe(stored);
        details.GetProperty("contentType").GetString().ShouldBe(contentType);
    }

    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task AFileNameThatIsOnlyAPathIsRefused(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);

        using var response = await api.UploadAsync(Callers.OnboardingSvc, "customer-1", "docs/", Bytes.Of(10), FilesApi.NewKey());

        await response.ShouldBeProblemDetailsAsync(HttpStatusCode.BadRequest);
        api.Host.LogCapture.Entries.ShouldContain(e => e.Message == "Upload refused: name, by onboarding-svc");
        (await api.FileNamesAsync("customer-1")).ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task BytesAProviderCannotFindAnswer500(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var fileId = await api.HasFileAsync("customer-1", "passport.pdf");
        api.Host.Blob.ReadFailure = new FileNotFoundException("simulated: no such blob");

        using var response = await api.DownloadAsync(Callers.ReportSvc, "customer-1", fileId);

        await response.ShouldBeProblemDetailsAsync(HttpStatusCode.InternalServerError);
        api.Host.LogCapture.Entries.ShouldContain(e =>
            e.Level == LogLevel.Error && e.Message == $"Bytes missing for file {fileId} under storage key files/{fileId}.pdf");
    }
}
