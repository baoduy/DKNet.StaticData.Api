using System.Diagnostics;
using System.Text.RegularExpressions;
using DKNet.StaticData.App.Tests.Files.Support;
using DKNet.StaticData.App.TestSupport;
using Microsoft.Extensions.Logging;

namespace DKNet.StaticData.App.Tests.Files;

/// <summary>
/// Spec DRK-2206 §5 "Logs": 1 Information entry per file action and per refused upload, an Error entry when a
/// dependency cannot be reached, the trace id and caller id on every entry of a call, and never the owner, the file
/// name or the bytes in a log entry or a trace.
/// </summary>
/// <remarks>
/// The spec fixes no message wording, so an entry is recognised by its level, the spec's event phrase at the start of
/// its message ("File uploaded", "Upload refused"), and values it carries exactly: the file id, the caller id, the
/// reason and the trace id the call was sent with (its <c>traceparent</c>).
/// </remarks>
public sealed class FileLogTests(FilesHost hosts) : IClassFixture<FilesHost>
{
    public static IEnumerable<object[]> FileActions => Databases.With(
        ["uploads \"visa.pdf\"", "File uploaded"],
        ["downloads \"passport.pdf\"", "File downloaded"],
        ["deletes \"passport.pdf\"", "File deleted"]);

    /// <summary>Scenario Outline: Each file action writes one log entry without personal data.</summary>
    [Theory]
    [MemberData(nameof(FileActions))]
    public async Task EachFileActionWritesOneLogEntryWithoutPersonalData(TestDatabase database, string action, string @event)
    {
        var api = await hosts.OnAsync(database);
        var passport = await api.HasFileAsync("customer-1", "passport.pdf");
        api.Host.LogCapture.Clear();
        var traceId = ActivityTraceId.CreateRandom().ToHexString();
        var traceParent = $"00-{traceId}-{ActivitySpanId.CreateRandom().ToHexString()}-01";
        using var traces = new TestTraceCapture();

        var (fileId, actedOn) = action switch
        {
            "uploads \"visa.pdf\"" => (await UploadedFileIdAsync(api, traceParent), "visa.pdf"),
            "downloads \"passport.pdf\"" => (await ExpectAsync(api.SendAsync(HttpMethod.Get,
                $"{FilesApi.Route}/{passport}/content{FilesApi.OwnerQuery("customer-1")}", Callers.OnboardingSvc,
                traceParent: traceParent), HttpStatusCode.OK, passport), "passport.pdf"),
            "deletes \"passport.pdf\"" => (await ExpectAsync(api.SendAsync(HttpMethod.Delete,
                $"{FilesApi.Route}/{passport}{FilesApi.OwnerQuery("customer-1")}", Callers.OnboardingSvc,
                traceParent: traceParent), HttpStatusCode.NoContent, passport), "passport.pdf"),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        };

        var entries = api.Host.LogCapture.Entries;
        var entry = entries.Where(e => e.Level == LogLevel.Information && e.Message.StartsWith(@event, StringComparison.Ordinal))
            .ShouldHaveSingleItem();
        entry.Names(fileId.ToString()).ShouldBeTrue($"\"{entry.Message}\" does not name file id {fileId}");
        entry.Names("onboarding-svc").ShouldBeTrue($"\"{entry.Message}\" does not name the caller id");
        entry.Names(traceId).ShouldBeTrue($"\"{entry.Message}\" does not carry the call's trace id {traceId}");
        foreach (var personal in new[] { "customer-1", "passport.pdf", actedOn })
        {
            entries.ShouldNotContain(e => e.Holds(personal), $"a log entry holds \"{personal}\"");
            traces.Holds(personal).ShouldBeFalse($"a trace holds \"{personal}\"");
        }
    }

    // each upload's answer is the one the Upload scenarios give it
    public static IEnumerable<object[]> RefusedUploads => Databases.With(
        ["\"page.html\"", "extension", HttpStatusCode.BadRequest],
        ["\"scan.pdf\" of 50,000,001 bytes", "size", HttpStatusCode.RequestEntityTooLarge],
        ["a form with 2 file parts", "form", HttpStatusCode.BadRequest]);

    /// <summary>Scenario Outline: A refused upload writes one log entry with its reason.</summary>
    [Theory]
    [MemberData(nameof(RefusedUploads))]
    public async Task ARefusedUploadWritesOneLogEntryWithItsReason(
        TestDatabase database, string upload, string reason, HttpStatusCode refusal)
    {
        var api = await hosts.OnAsync(database);
        var (form, fileNames) = upload switch
        {
            "\"page.html\"" => (FilesApi.Form(("page.html", Bytes.Of(1024), "text/html")), new[] { "page.html" }),
            "\"scan.pdf\" of 50,000,001 bytes" => (FilesApi.Form(("scan.pdf", Bytes.Of(50_000_001), "application/pdf")), ["scan.pdf"]),
            "a form with 2 file parts" => (FilesApi.Form(
                ("passport.pdf", Bytes.Of(1024), "application/pdf"),
                ("visa.pdf", Bytes.Of(1024), "application/pdf")), ["passport.pdf", "visa.pdf"]),
            _ => throw new ArgumentOutOfRangeException(nameof(upload), upload, null)
        };

        using var response = await api.SendAsync(
            HttpMethod.Post, FilesApi.Route + FilesApi.OwnerQuery("customer-1"), Callers.OnboardingSvc, form, FilesApi.NewKey());

        await response.ShouldHaveStatusAsync(refusal);
        var entries = api.Host.LogCapture.Entries;
        var entry = entries
            .Where(e => e.Level == LogLevel.Information && e.Message.StartsWith("Upload refused", StringComparison.Ordinal))
            .ShouldHaveSingleItem();
        entry.Names(reason).ShouldBeTrue($"\"{entry.Message}\" does not name the reason \"{reason}\"");
        entry.Names("onboarding-svc").ShouldBeTrue($"\"{entry.Message}\" does not name the caller id");
        foreach (var personal in fileNames.Append("customer-1"))
        {
            entries.ShouldNotContain(e => e.Holds(personal), $"a log entry holds \"{personal}\"");
        }
    }

    public static IEnumerable<object[]> UnreachableDependencies => Databases.With(
        ["the database", "lists the files"],
        ["blob storage", "downloads \"passport.pdf\""]);

    /// <summary>Scenario Outline: A dependency that cannot be reached is logged as an error.</summary>
    [Theory]
    [MemberData(nameof(UnreachableDependencies))]
    public async Task ADependencyThatCannotBeReachedIsLoggedAsAnError(TestDatabase database, string dependency, string action)
    {
        var api = dependency == "the database" ? await hosts.OnOwnServerAsync(database) : await hosts.OnAsync(database);
        var fileId = await api.HasFileAsync("customer-1", "passport.pdf");
        api.Host.LogCapture.Clear();
        if (dependency == "the database")
        {
            await api.Server.StopAsync();
        }
        else
        {
            api.Host.Blob.ReadFailure = new IOException("simulated: blob storage cannot be reached");
        }

        using var response = action == "lists the files"
            ? await api.ListAsync(Callers.ReportSvc, "customer-1")
            : await api.DownloadAsync(Callers.ReportSvc, "customer-1", fileId);

        ((int)response.StatusCode).ShouldBeInRange(500, 599, await AnswerAssertions.DescribeAsync(response));
        var named = dependency == "the database" ? "database" : "blob storage";
        api.Host.LogCapture.Entries.ShouldContain(
            e => e.Level == LogLevel.Error &&
                 !e.Category.StartsWith("Microsoft.", StringComparison.Ordinal) &&
                 e.Texts().Any(t => t.Contains(named, StringComparison.OrdinalIgnoreCase)) &&
                 e.Texts().Any(t => (dependency == "blob storage" ? BlobFailure() : AnyException()).IsMatch(t)),
            $"no Error log entry names {dependency} and the exception type");
    }

    private static async Task<Guid> UploadedFileIdAsync(FilesApi api, string traceParent)
    {
        using var response = await api.SendAsync(
            HttpMethod.Post,
            FilesApi.Route + FilesApi.OwnerQuery("customer-1"),
            Callers.OnboardingSvc,
            FilesApi.Form(("visa.pdf", Bytes.Of(1024), "application/pdf")),
            FilesApi.NewKey(),
            traceParent);
        await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
        return (await response.JsonAsync()).GetProperty("fileId").GetGuid();
    }

    private static async Task<Guid> ExpectAsync(Task<HttpResponseMessage> call, HttpStatusCode status, Guid fileId)
    {
        using var response = await call;
        await response.ShouldHaveStatusAsync(status);
        return fileId;
    }

    /// <summary>The type of the exception the blob seam throws.</summary>
    private static Regex BlobFailure() => new(@"\bIOException\b", RegexOptions.None, TimeSpan.FromSeconds(1));

    private static Regex AnyException() => new(@"\b\w+Exception\b", RegexOptions.None, TimeSpan.FromSeconds(1));
}
