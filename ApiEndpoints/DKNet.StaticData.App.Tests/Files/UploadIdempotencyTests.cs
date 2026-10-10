using DKNet.StaticData.App.Tests.Files.Support;
using DKNet.StaticData.App.TestSupport;

namespace DKNet.StaticData.App.Tests.Files;

/// <summary>
/// Spec DRK-2206 §5 "Upload", idempotency: records live in the service's own database, scoped by caller, owner, route
/// and key; a repeat after a 201 replays it, a repeat while the first runs or within 330 seconds of a failed first
/// request answers 409.
/// </summary>
public sealed class UploadIdempotencyTests(FilesHost hosts) : IClassFixture<FilesHost>
{
    /// <summary>Scenario Outline: A repeated upload after success replays the first answer.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task ARepeatedUploadAfterSuccessReplaysTheFirstAnswer(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        using var first = await api.UploadAsync(Callers.OnboardingSvc, "customer-1", "passport.pdf", Bytes.Of(1024), "upload-1");
        await first.ShouldHaveStatusAsync(HttpStatusCode.Created);

        using var again = await api.UploadAsync(Callers.OnboardingSvc, "customer-1", "passport.pdf", Bytes.Of(1024), "upload-1");

        await again.ShouldHaveStatusAsync(HttpStatusCode.Created);
        (await again.Content.ReadAsStringAsync()).ShouldBe(await first.Content.ReadAsStringAsync());
        again.Headers.Location.ShouldBeNull();
        again.Headers.ETag.ShouldBeNull();
        (await api.FileNamesAsync("customer-1")).ShouldBe(["passport.pdf"]);
    }

    /// <summary>Scenario: A repeated upload while the first still runs is refused.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task ARepeatedUploadWhileTheFirstStillRunsIsRefused(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var gate = new BlobGate();
        api.Host.Blob.SaveGate = gate;
        var first = api.UploadAsync(Callers.OnboardingSvc, "customer-1", "scan.pdf", Bytes.Of(1024), "upload-2");
        // "is uploading": the first upload has reached blob storage and is held there
        if (await Task.WhenAny(gate.Entered, first, Task.Delay(TimeSpan.FromSeconds(30))) != gate.Entered)
        {
            gate.Open();
            using var unheld = first.IsCompleted ? await first : null;
            throw new ShouldAssertException(unheld is null
                ? "the first upload never reached blob storage"
                : $"the first upload never reached blob storage: {await AnswerAssertions.DescribeAsync(unheld)}");
        }

        using (var second = await api.UploadAsync(Callers.OnboardingSvc, "customer-1", "scan.pdf", Bytes.Of(1024), "upload-2"))
        {
            gate.Open();
            await second.ShouldHaveStatusAsync(HttpStatusCode.Conflict);
        }

        using (var firstAnswer = await first)
        {
            await firstAnswer.ShouldHaveStatusAsync(HttpStatusCode.Created);
        }

        (await api.FileNamesAsync("customer-1")).ShouldBe(["scan.pdf"]);
    }

    /// <summary>Scenario: The same key for another owner is a new upload.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task TheSameKeyForAnotherOwnerIsANewUpload(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        using var first = await api.UploadAsync(Callers.OnboardingSvc, "customer-1", "passport.pdf", Bytes.Of(1024), "upload-3");
        await first.ShouldHaveStatusAsync(HttpStatusCode.Created);

        using var response = await api.UploadAsync(Callers.OnboardingSvc, "customer-2", "passport.pdf", Bytes.Of(1024), "upload-3");

        await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
        (await response.JsonAsync()).GetProperty("fileId").GetGuid()
            .ShouldNotBe((await first.JsonAsync()).GetProperty("fileId").GetGuid());
        (await api.FileNamesAsync("customer-2")).ShouldBe(["passport.pdf"]);
    }

    /// <summary>Scenario: The same key from another caller is a new upload.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task TheSameKeyFromAnotherCallerIsANewUpload(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        using var first = await api.UploadAsync(Callers.OnboardingSvc, "customer-1", "passport.pdf", Bytes.Of(1024), "upload-1");
        await first.ShouldHaveStatusAsync(HttpStatusCode.Created);

        using var response = await api.UploadAsync(Callers.IngestSvc, "customer-1", "visa.pdf", Bytes.Of(1024), "upload-1");

        await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
        (await response.JsonAsync()).GetProperty("fileId").GetGuid()
            .ShouldNotBe((await first.JsonAsync()).GetProperty("fileId").GetGuid());
        (await api.FileNamesAsync("customer-1")).ShouldBe(["visa.pdf", "passport.pdf"], ignoreOrder: true);
    }

    /// <summary>Scenario: A repeat sent to another replica replays the first answer.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task ARepeatSentToAnotherReplicaReplaysTheFirstAnswer(TestDatabase database)
    {
        var firstReplica = await hosts.OnAsync(database);
        var secondReplica = await hosts.ReplicaOfAsync(firstReplica);
        using var first = await firstReplica.UploadAsync(
            Callers.OnboardingSvc, "customer-1", "passport.pdf", Bytes.Of(1024), "upload-5");
        await first.ShouldHaveStatusAsync(HttpStatusCode.Created);

        using var repeat = await secondReplica.UploadAsync(
            Callers.OnboardingSvc, "customer-1", "passport.pdf", Bytes.Of(1024), "upload-5");

        await repeat.ShouldHaveStatusAsync(HttpStatusCode.Created);
        (await repeat.Content.ReadAsStringAsync()).ShouldBe(await first.Content.ReadAsStringAsync());
        (await firstReplica.FileNamesAsync("customer-1")).ShouldBe(["passport.pdf"]);
    }

    public static IEnumerable<object[]> RepeatsAfterAnError => Databases.With(
        [10, "409"],
        [331, "201, the upload runs again"]);

    /// <summary>Scenario Outline: A repeat after an error answer waits for the reservation to end.</summary>
    [Theory]
    [MemberData(nameof(RepeatsAfterAnError))]
    public async Task ARepeatAfterAnErrorAnswerWaitsForTheReservationToEnd(TestDatabase database, int seconds, string status)
    {
        var api = await hosts.OnAsync(database);
        api.Host.Blob.SaveFailure = new IOException("simulated: blob storage cannot be reached");
        using (var failed = await api.UploadAsync(Callers.OnboardingSvc, "customer-1", "scan.pdf", Bytes.Of(1024), "upload-4"))
        {
            await failed.ShouldHaveStatusAsync(HttpStatusCode.ServiceUnavailable);
        }

        api.Host.Blob.SaveFailure = null; // blob storage works again
        await IdempotencyRecords.AgeAsync(api, seconds);

        using var repeat = await api.UploadAsync(Callers.OnboardingSvc, "customer-1", "scan.pdf", Bytes.Of(1024), "upload-4");

        if (status == "409")
        {
            await repeat.ShouldHaveStatusAsync(HttpStatusCode.Conflict);
            (await api.FileNamesAsync("customer-1")).ShouldBeEmpty();
        }
        else
        {
            await repeat.ShouldHaveStatusAsync(HttpStatusCode.Created);
            (await api.FileNamesAsync("customer-1")).ShouldBe(["scan.pdf"]);
        }
    }
}
