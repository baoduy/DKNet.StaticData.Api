using DKNet.StaticData.App.Tests.Files.Support;
using DKNet.StaticData.App.TestSupport;
using Microsoft.Extensions.Logging;

namespace DKNet.StaticData.App.Tests.Files;

/// <summary>
/// Spec DRK-2206 §5 "Delete": the file's details, then its bytes, answering 204 — even when the bytes cannot be
/// deleted, which is logged — and spec DRK-2235 §5 "Slice 2 fixes": 2 deletes of one file at the same moment both
/// answer 204.
/// </summary>
public sealed class DeleteTests(FilesHost hosts) : IClassFixture<FilesHost>
{
    /// <summary>Scenario: A caller deletes a file.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task ACallerDeletesAFile(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var fileId = await api.HasFileAsync("customer-1", "passport.pdf");
        api.StoredBlobs().ShouldBe([$"files/{fileId}.pdf"]);

        using var response = await api.DeleteAsync(Callers.OnboardingSvc, "customer-1", fileId);

        await response.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        using (var read = await api.ReadAsync(Callers.OnboardingSvc, "customer-1", fileId))
        {
            await read.ShouldHaveStatusAsync(HttpStatusCode.NotFound);
        }

        using (var download = await api.DownloadAsync(Callers.OnboardingSvc, "customer-1", fileId))
        {
            await download.ShouldHaveStatusAsync(HttpStatusCode.NotFound);
        }

        using (var deleteAgain = await api.DeleteAsync(Callers.OnboardingSvc, "customer-1", fileId))
        {
            await deleteAgain.ShouldHaveStatusAsync(HttpStatusCode.NotFound);
        }

        api.StoredBlobs().ShouldBeEmpty();
    }

    /// <summary>Scenario: A failed byte delete still deletes the file.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task AFailedByteDeleteStillDeletesTheFile(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var fileId = await api.HasFileAsync("customer-1", "passport.pdf");
        var storageKey = $"files/{fileId}.pdf";
        api.Host.Blob.DeleteFailure = new IOException("simulated: blob storage fails to delete");

        using var response = await api.DeleteAsync(Callers.OnboardingSvc, "customer-1", fileId);

        await response.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        api.Host.LogCapture.Entries.ShouldContain(
            e => e.Level == LogLevel.Warning && e.Names(fileId.ToString()) && e.Names(storageKey),
            $"no Warning log entry names file id {fileId} and storage key {storageKey}");
        (await api.FileNamesAsync("customer-1")).ShouldBeEmpty();
    }

    /// <summary>Scenario Outline: Two deletes of one file at the same moment both succeed.</summary>
    /// <remarks>
    /// "At the same moment": each delete is held just before its save until both have reached it, so both have read the
    /// file before either deletes it.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task TwoDeletesOfOneFileAtTheSameMomentBothSucceed(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var fileId = await api.HasFileAsync("customer-1", "passport.pdf");
        api.StoredBlobs().ShouldBe([$"files/{fileId}.pdf"]);
        var arrived = 0;
        var bothArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        api.Host.BeforeSave.Run(async () =>
        {
            if (Interlocked.Increment(ref arrived) == 2)
            {
                bothArrived.SetResult();
            }

            await bothArrived.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }, times: 2);

        var answers = await Task.WhenAll(
            api.DeleteAsync(Callers.OnboardingSvc, "customer-1", fileId),
            api.DeleteAsync(Callers.OnboardingSvc, "customer-1", fileId));

        try
        {
            answers.Select(a => a.StatusCode).ShouldBe([HttpStatusCode.NoContent, HttpStatusCode.NoContent],
                string.Join("; ", await Task.WhenAll(answers.Select(AnswerAssertions.DescribeAsync))));
        }
        finally
        {
            foreach (var answer in answers)
            {
                answer.Dispose();
            }
        }

        using (var read = await api.ReadAsync(Callers.OnboardingSvc, "customer-1", fileId))
        {
            await read.ShouldHaveStatusAsync(HttpStatusCode.NotFound);
        }

        api.StoredBlobs().ShouldBeEmpty();
    }
}
