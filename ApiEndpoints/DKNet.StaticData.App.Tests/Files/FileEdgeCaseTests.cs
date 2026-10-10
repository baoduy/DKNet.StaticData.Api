using DKNet.StaticData.App.Tests.Files.Support;
using DKNet.StaticData.App.TestSupport;
using Microsoft.Extensions.Logging;

namespace DKNet.StaticData.App.Tests.Files;

/// <summary>
/// Brief DRK-2209 §6a rows the spec's scenarios leave open — D5 (file names) and D9 (a provider that reports missing
/// bytes by throwing <see cref="FileNotFoundException" />, as the cloud providers do, rather than returning none) — and
/// §9 Q2: a database that refuses a save answers 500, one that cannot be reached 503; each is logged as an error.
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
    public async Task TheUploadAndReadAnswersCarryTheVersionAsETag(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        using var upload = await api.UploadAsync(Callers.OnboardingSvc, "customer-1", "passport.pdf", Bytes.Of(10), FilesApi.NewKey());
        await upload.ShouldHaveStatusAsync(HttpStatusCode.Created);
        upload.Headers.ETag.ShouldNotBeNull().Tag.ShouldBe("\"1\"");

        using var read = await api.ReadAsync(Callers.ReportSvc, "customer-1", (await upload.JsonAsync()).GetProperty("fileId").GetGuid());

        await read.ShouldHaveStatusAsync(HttpStatusCode.OK);
        read.Headers.ETag.ShouldNotBeNull().Tag.ShouldBe("\"1\"");
    }

    [Fact]
    public async Task ABodyOverTheUploadLimitIsRefusedForItsSize()
    {
        var api = await hosts.OnAsync(TestDatabase.Postgres);

        using var response = await api.UploadAsync(
            Callers.OnboardingSvc, "customer-1", "scan.pdf", Bytes.Of(51_000_001), FilesApi.NewKey());

        await response.ShouldBeProblemDetailsAsync(HttpStatusCode.RequestEntityTooLarge);
        api.Host.LogCapture.Entries.ShouldContain(e => e.Message == "Upload refused: size, by onboarding-svc");
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
    public async Task AnUploadThatIsNotAFormIsLoggedAsRefused(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);

        using var response = await api.SendAsync(HttpMethod.Post, FilesApi.Route + FilesApi.OwnerQuery("customer-1"),
            Callers.OnboardingSvc, new StringContent("{}", System.Text.Encoding.UTF8, "application/json"), FilesApi.NewKey());

        await response.ShouldBeProblemDetailsAsync(HttpStatusCode.UnsupportedMediaType);
        api.Host.LogCapture.Entries.ShouldContain(e => e.Message == "Upload refused: form, by onboarding-svc");
    }

    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task ABlobStorageFailureOnUploadIsLoggedAndItsKeyCleared(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        api.Host.Blob.SaveFailure = new IOException("simulated: blob storage cannot be reached");

        using var response = await api.UploadAsync(Callers.OnboardingSvc, "customer-1", "passport.pdf", Bytes.Of(10), FilesApi.NewKey());

        await response.ShouldBeProblemDetailsAsync(HttpStatusCode.ServiceUnavailable);
        api.Host.LogCapture.Entries.ShouldContain(e =>
            e.Level == LogLevel.Error && e.Message == "Blob storage cannot be reached: IOException");
        api.Host.Blob.SavedKeys.ShouldBeEmpty();
        api.Host.Blob.DeletedKeys.ShouldHaveSingleItem().ShouldStartWith("files/");
    }

    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task ASaveTheDatabaseRefusesAnswers500(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        api.Host.SaveFailure.Enabled = true;

        using var response = await api.UploadAsync(Callers.OnboardingSvc, "customer-1", "passport.pdf", Bytes.Of(10), FilesApi.NewKey());

        await response.ShouldBeProblemDetailsAsync(HttpStatusCode.InternalServerError);
        api.Host.LogCapture.Entries.ShouldContain(e =>
            e.Level == LogLevel.Error && e.Message == "The database failed: DbUpdateException");
    }

    /// <remarks>
    /// SQL Server only: on Postgres, EF Core's retries on a stopped server outlast the 30-second request timeout, which
    /// answers 504 first.
    /// </remarks>
    [Fact]
    public async Task ADatabaseThatCannotBeReachedAnswers503()
    {
        var api = await hosts.OnOwnServerAsync(TestDatabase.SqlServer);
        var fileId = await api.HasFileAsync("customer-1", "passport.pdf");
        await api.Server.StopAsync();

        using var response = await api.ReadAsync(Callers.ReportSvc, "customer-1", fileId);

        await response.ShouldBeProblemDetailsAsync(HttpStatusCode.ServiceUnavailable);
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
