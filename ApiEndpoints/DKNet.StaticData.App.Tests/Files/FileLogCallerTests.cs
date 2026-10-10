using DKNet.StaticData.App.Tests.Files.Support;
using DKNet.StaticData.App.TestSupport;
using Microsoft.Extensions.Logging;

namespace DKNet.StaticData.App.Tests.Files;

/// <summary>
/// Spec DRK-2206 §3 Logs: every entry written for a call carries the caller id — the dependency errors too — and none
/// carries the owner or the file name.
/// </summary>
public sealed class FileLogCallerTests(FilesHost hosts) : IClassFixture<FilesHost>
{
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task TheErrorForUnreachableBlobStorageOnDownloadNamesTheCaller(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var fileId = await api.HasFileAsync("customer-1", "passport.pdf");
        api.Host.LogCapture.Clear();
        api.Host.Blob.ReadFailure = new IOException("simulated: blob storage cannot be reached");

        using var response = await api.DownloadAsync(Callers.ReportSvc, "customer-1", fileId);

        await response.ShouldBeProblemDetailsAsync(HttpStatusCode.ServiceUnavailable);
        var error = api.Host.LogCapture.Entries
            .Where(e => e.Level == LogLevel.Error && e.Message == "Blob storage cannot be reached: IOException")
            .ShouldHaveSingleItem();
        error.Names("report-svc").ShouldBeTrue();
        NoEntryHoldsTheOwnerOrTheFileName(api);
    }

    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task TheErrorForARefusedDatabaseSaveNamesTheCaller(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        api.Host.SaveFailure.Enabled = true;

        using var response = await api.UploadAsync(Callers.OnboardingSvc, "customer-1", "passport.pdf", Bytes.Of(10), FilesApi.NewKey());

        await response.ShouldBeProblemDetailsAsync(HttpStatusCode.InternalServerError);
        var error = api.Host.LogCapture.Entries
            .Where(e => e.Level == LogLevel.Error && e.Message == "The database failed: DbUpdateException")
            .ShouldHaveSingleItem();
        error.Names("onboarding-svc").ShouldBeTrue();
        NoEntryHoldsTheOwnerOrTheFileName(api);
    }

    private static void NoEntryHoldsTheOwnerOrTheFileName(FilesApi api)
    {
        foreach (var personal in new[] { "customer-1", "passport.pdf" })
        {
            api.Host.LogCapture.Entries.ShouldNotContain(e => e.Holds(personal), $"a log entry holds \"{personal}\"");
        }
    }
}
