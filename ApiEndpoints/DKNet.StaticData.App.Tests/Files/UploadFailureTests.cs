using DKNet.StaticData.App.Tests.Files.Support;
using DKNet.StaticData.App.TestSupport;
using Microsoft.Extensions.Logging;

namespace DKNet.StaticData.App.Tests.Files;

/// <summary>
/// Spec DRK-2206 §5 "Upload", failures: a file's details exist only after its bytes are stored, and bytes stored for
/// details that could not be saved are removed, or logged when they cannot be.
/// </summary>
public sealed class UploadFailureTests(FilesHost hosts) : IClassFixture<FilesHost>
{
    /// <summary>Scenario: A blob storage failure on upload stores no file.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task ABlobStorageFailureOnUploadStoresNoFile(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        api.Host.Blob.SaveFailure = new IOException("simulated: blob storage cannot be reached");

        using var response = await api.UploadAsync(Callers.OnboardingSvc, "customer-1", "passport.pdf", Bytes.Of(1024), FilesApi.NewKey());

        await response.ShouldHaveStatusAsync(HttpStatusCode.ServiceUnavailable);
        api.Host.Blob.SaveFailure = null;
        (await api.FileNamesAsync("customer-1")).ShouldBeEmpty();
    }

    /// <summary>Scenario: Failing to save the file details removes the stored bytes.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task FailingToSaveTheFileDetailsRemovesTheStoredBytes(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        api.Host.SaveFailure.Enabled = true;

        using var response = await api.UploadAsync(Callers.OnboardingSvc, "customer-1", "passport.pdf", Bytes.Of(1024), FilesApi.NewKey());

        ((int)response.StatusCode).ShouldBeInRange(500, 599, await AnswerAssertions.DescribeAsync(response));
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        api.Host.SaveFailure.Enabled = false;
        (await api.FileNamesAsync("customer-1")).ShouldBeEmpty();
        // the bytes were stored first, then removed: the local folder holds no bytes for that upload
        var stored = api.Host.Blob.SavedKeys.ShouldHaveSingleItem();
        api.Host.Blob.DeletedKeys.ShouldBe([stored]);
        api.StoredBlobs().ShouldBeEmpty();
    }

    /// <summary>Scenario: Bytes left behind after a failed clean-up are logged.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task BytesLeftBehindAfterAFailedCleanUpAreLogged(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        api.Host.SaveFailure.Enabled = true;
        api.Host.Blob.DeleteFailure = new IOException("simulated: blob storage fails to delete");

        using var response = await api.UploadAsync(Callers.OnboardingSvc, "customer-1", "passport.pdf", Bytes.Of(1024), FilesApi.NewKey());

        ((int)response.StatusCode).ShouldBeInRange(500, 599, await AnswerAssertions.DescribeAsync(response));
        var storageKey = api.Host.Blob.SavedKeys.ShouldHaveSingleItem();
        var fileId = FileIdOf(storageKey);
        api.Host.LogCapture.Entries.ShouldContain(
            e => e.Level == LogLevel.Warning && e.Names(fileId) && e.Names(storageKey),
            $"no Warning log entry names file id {fileId} and storage key {storageKey}");
    }

    /// <summary>The file id a storage key <c>files/&lt;file id&gt;&lt;extension&gt;</c> is built from.</summary>
    internal static string FileIdOf(string storageKey)
    {
        storageKey.ShouldStartWith("files/");
        return Path.GetFileNameWithoutExtension(storageKey);
    }
}
