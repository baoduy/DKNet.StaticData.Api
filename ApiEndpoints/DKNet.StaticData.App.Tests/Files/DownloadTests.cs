using DKNet.StaticData.App.Tests.Files.Support;
using DKNet.StaticData.App.TestSupport;
using Microsoft.Extensions.Logging;

namespace DKNet.StaticData.App.Tests.Files;

/// <summary>
/// Spec DRK-2206 §5 "Download": the stored bytes as an attachment with the stored content type and size, a safe file
/// name and <c>nosniff</c>; 500 when the bytes are missing, 503 when blob storage cannot be reached.
/// </summary>
public sealed class DownloadTests(FilesHost hosts) : IClassFixture<FilesHost>
{
    /// <summary>Scenario: A caller downloads the exact bytes with a safe file name.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task ACallerDownloadsTheExactBytesWithASafeFileName(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var bytes = Bytes.Of(2048);
        var fileId = await api.HasFileAsync("customer-1", "hồ-sơ.pdf", bytes);

        using var response = await api.DownloadAsync(Callers.ReportSvc, "customer-1", fileId);

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        (await response.Content.ReadAsByteArrayAsync()).ShouldBe(bytes);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/pdf");
        response.Content.Headers.ContentLength.ShouldBe(2048);
        var disposition = response.Content.Headers.ContentDisposition.ShouldNotBeNull();
        disposition.DispositionType.ShouldBe("attachment");
        disposition.FileNameStar.ShouldBe("hồ-sơ.pdf");
        var fallback = disposition.FileName.ShouldNotBeNull().Trim('"');
        fallback.ShouldNotBeEmpty();
        fallback.ShouldAllBe(c => c < 128, "the fallback file name is ASCII only");
        response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
    }

    /// <summary>Scenario: Missing bytes for an existing file answer 500.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task MissingBytesForAnExistingFileAnswer500(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var fileId = await api.HasFileAsync("customer-1", "passport.pdf");
        var storageKey = $"files/{fileId}.pdf";
        var bytesPath = Path.Combine(api.Host.BlobRoot, "files", $"{fileId}.pdf");
        File.Exists(bytesPath).ShouldBeTrue();
        File.Delete(bytesPath); // its bytes were removed from blob storage

        using var response = await api.DownloadAsync(Callers.ReportSvc, "customer-1", fileId);

        await response.ShouldHaveStatusAsync(HttpStatusCode.InternalServerError);
        api.Host.LogCapture.Entries.ShouldContain(
            e => e.Level == LogLevel.Error && e.Names(fileId.ToString()) && e.Names(storageKey),
            $"no Error log entry names file id {fileId} and storage key {storageKey}");
    }

    /// <summary>Scenario: Blob storage that cannot be reached answers 503 on download.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task BlobStorageThatCannotBeReachedAnswers503OnDownload(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var fileId = await api.HasFileAsync("customer-1", "passport.pdf");
        api.Host.Blob.ReadFailure = new IOException("simulated: blob storage cannot be reached");

        using var response = await api.DownloadAsync(Callers.ReportSvc, "customer-1", fileId);

        await response.ShouldHaveStatusAsync(HttpStatusCode.ServiceUnavailable);
    }
}
