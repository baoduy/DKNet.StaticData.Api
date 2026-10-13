using System.Diagnostics;
using System.Text.RegularExpressions;
using DKNet.StaticData.App.Tests.FileGroups.Support;
using DKNet.StaticData.App.Tests.Files.Support;
using DKNet.StaticData.App.TestSupport;
using Microsoft.Extensions.Logging;

namespace DKNet.StaticData.App.Tests.Files;

/// <summary>
/// Brief DRK-2209 §6a rows the spec's scenarios leave open — D5 (file names) and D9 (a provider that reports missing
/// bytes by throwing <see cref="FileNotFoundException" />, as the cloud providers do, rather than returning none) — and
/// §9 Q2: a database that refuses a save answers 500, one that cannot be reached 503 — on both databases and on the
/// group and link routes too (spec DRK-2235 §5) — each logged as an error.
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
        // the key of the new file id the upload made: files/<file id>.pdf
        api.Host.Blob.DeletedKeys.ShouldHaveSingleItem()
            .ShouldMatch(@"^files/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\.pdf$");
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

    public static IEnumerable<object[]> UnreachableDatabaseActions => Databases.With(
        ["lists the files"],
        ["lists the groups"],
        ["creates the group \"Pack 2\""],
        ["links \"passport.pdf\" to \"Onboarding pack\" with the version tag \"1\""]);

    /// <summary>Scenario Outline: A database that cannot be reached answers 503 (spec DRK-2235 §5, Slice 2 fixes).</summary>
    /// <remarks>
    /// The group is made only for the rows on a group route or the link route, which need one; the file rows run on the
    /// slice 2 routes alone.
    /// </remarks>
    [Theory]
    [MemberData(nameof(UnreachableDatabaseActions))]
    public async Task ADatabaseThatCannotBeReachedAnswers503(TestDatabase database, string action)
    {
        var api = await hosts.OnOwnServerAsync(database);
        var fileId = await api.HasFileAsync("customer-1", "passport.pdf");
        var groupId = action == "lists the files" ? Guid.Empty : await api.HasGroupAsync("customer-1", "Onboarding pack");
        await api.Server.StopAsync();
        api.Host.LogCapture.Clear();
        var traceId = ActivityTraceId.CreateRandom().ToHexString();
        var traceParent = $"00-{traceId}-{ActivitySpanId.CreateRandom().ToHexString()}-01";
        var clock = Stopwatch.StartNew();

        using var response = action switch
        {
            "lists the files" => await api.SendAsync(HttpMethod.Get, FilesApi.Route + FilesApi.OwnerQuery("customer-1"),
                Callers.OnboardingSvc, traceParent: traceParent),
            "lists the groups" => await api.ListGroupsAsync(Callers.OnboardingSvc, "customer-1", traceParent: traceParent),
            "creates the group \"Pack 2\"" => await api.CreateGroupAsync(
                Callers.OnboardingSvc, "customer-1", FileGroupsApi.GroupBody("Pack 2"), FilesApi.NewKey(), traceParent),
            "links \"passport.pdf\" to \"Onboarding pack\" with the version tag \"1\"" => await api.LinkAsync(
                Callers.OnboardingSvc, "customer-1", fileId, FileGroupsApi.LinkBody(groupId), "\"1\"", traceParent),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        };

        clock.Stop();
        await response.ShouldBeProblemDetailsAsync(HttpStatusCode.ServiceUnavailable);
        clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(30));
        var error = api.Host.LogCapture.Entries
            .Where(e => e.Level == LogLevel.Error && DatabaseFailed().IsMatch(e.Message))
            .ShouldHaveSingleItem("no single Error log entry \"The database failed: <exception type>\"");
        error.Names(traceId).ShouldBeTrue($"\"{error.Message}\" does not carry the call's trace id {traceId}");
        error.Names("onboarding-svc").ShouldBeTrue($"\"{error.Message}\" does not name the caller id");
    }

    /// <summary>The file routes' Error entry for the database: it names the database and the exception type.</summary>
    private static Regex DatabaseFailed() => new(@"^The database failed: \w+Exception$", RegexOptions.None, TimeSpan.FromSeconds(1));

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
