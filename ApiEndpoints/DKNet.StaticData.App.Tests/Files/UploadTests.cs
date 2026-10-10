using System.Net.Http.Json;
using System.Security.Cryptography;
using DKNet.StaticData.App.Tests.Files.Support;
using DKNet.StaticData.App.TestSupport;

namespace DKNet.StaticData.App.Tests.Files;

/// <summary>
/// Spec DRK-2206 §5 "Upload": the form, the file name and its extension, the size limit, the idempotency key's shape
/// and where the bytes are stored.
/// </summary>
public sealed class UploadTests(FilesHost hosts) : IClassFixture<FilesHost>
{
    /// <summary>Scenario Outline: A caller uploads a file.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task ACallerUploadsAFile(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var bytes = Bytes.Of(1024);

        using var response = await api.UploadAsync(Callers.OnboardingSvc, "customer-1", "passport.pdf", bytes, "upload-1");

        await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
        var details = await response.JsonAsync();
        var fileId = details.GetProperty("fileId").GetGuid();
        LocationPath(response).ShouldBe($"/v1/files/{fileId}");
        response.Headers.ETag?.Tag.ShouldBe("\"1\"");
        details.GetProperty("fileName").GetString().ShouldBe("passport.pdf");
        details.GetProperty("contentType").GetString().ShouldBe("application/pdf");
        details.GetProperty("sizeBytes").GetInt64().ShouldBe(1024);
        details.GetProperty("checksum").GetString().ShouldBe(Convert.ToHexStringLower(SHA256.HashData(bytes)));
        details.GetProperty("version").GetInt32().ShouldBe(1);
        // no storage key and no updated-by or updated-on field
        var fields = details.EnumerateObject().Select(p => p.Name).ToArray();
        fields.ShouldNotContain(f => f.Equals("storageKey", StringComparison.OrdinalIgnoreCase));
        fields.ShouldNotContain("updatedBy");
        fields.ShouldNotContain("updatedOn");
        (await response.Content.ReadAsStringAsync()).ShouldNotContain("files/");
    }

    public static IEnumerable<object[]> FileNames => Databases.With(
        ["passport.PDF", "201, stored as \"passport.PDF\" with \"application/pdf\""],
        ["C:\\docs\\passport.pdf", "201, stored as \"passport.pdf\""],
        ["page.html", "400 as problem details"],
        ["README", "400 as problem details"],
        ["a 256-character name.pdf", "400 as problem details"],
        ["a name.pdf with a tab character", "400 as problem details"]);

    /// <summary>Scenario Outline: The file name and its extension decide whether a file is stored.</summary>
    [Theory]
    [MemberData(nameof(FileNames))]
    public async Task TheFileNameAndItsExtensionDecideWhetherAFileIsStored(TestDatabase database, string name, string status)
    {
        var api = await hosts.OnAsync(database);
        var sent = name switch
        {
            "a 256-character name.pdf" => new string('n', 252) + ".pdf",
            "a name.pdf with a tab character" => "pass\tport.pdf",
            _ => name
        };

        using var response = await api.UploadAsync(Callers.OnboardingSvc, "customer-1", sent, Bytes.Of(1024), FilesApi.NewKey());

        switch (status)
        {
            case "201, stored as \"passport.PDF\" with \"application/pdf\"":
                await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
                var upper = await response.JsonAsync();
                upper.GetProperty("fileName").GetString().ShouldBe("passport.PDF");
                upper.GetProperty("contentType").GetString().ShouldBe("application/pdf");
                break;
            case "201, stored as \"passport.pdf\"":
                await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
                (await response.JsonAsync()).GetProperty("fileName").GetString().ShouldBe("passport.pdf");
                break;
            default:
                await response.ShouldBeProblemDetailsAsync(HttpStatusCode.BadRequest);
                (await api.FileNamesAsync("customer-1")).ShouldBeEmpty();
                break;
        }
    }

    /// <summary>Scenario: The content type comes from the extension, never from the caller.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task TheContentTypeComesFromTheExtensionNeverFromTheCaller(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);

        using var response = await api.UploadAsync(
            Callers.OnboardingSvc, "customer-1", "report.pdf", Bytes.Of(1024), FilesApi.NewKey(), declaredType: "text/html");

        await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
        (await response.JsonAsync()).GetProperty("contentType").GetString().ShouldBe("application/pdf");
    }

    public static IEnumerable<object[]> Sizes => Databases.With(
        [0, HttpStatusCode.BadRequest],
        [50_000_000, HttpStatusCode.Created],
        [50_000_001, HttpStatusCode.RequestEntityTooLarge]);

    /// <summary>Scenario Outline: The service limits the file size.</summary>
    [Theory]
    [MemberData(nameof(Sizes))]
    public async Task TheServiceLimitsTheFileSize(TestDatabase database, int size, HttpStatusCode status)
    {
        var api = await hosts.OnAsync(database);

        using var response = await api.UploadAsync(Callers.OnboardingSvc, "customer-1", "scan.pdf", Bytes.Of(size), FilesApi.NewKey());

        await response.ShouldHaveStatusAsync(status);
        (await api.FileNamesAsync("customer-1")).ShouldBe(status == HttpStatusCode.Created ? ["scan.pdf"] : []);
    }

    public static IEnumerable<object[]> Forms => Databases.With(
        ["a JSON body instead of a form", HttpStatusCode.UnsupportedMediaType],
        ["a form with no file part", HttpStatusCode.BadRequest],
        ["a form with 2 file parts", HttpStatusCode.BadRequest],
        ["a form with \"passport.pdf\" and a group id", HttpStatusCode.BadRequest]);

    /// <summary>Scenario Outline: The upload form must hold exactly 1 file.</summary>
    [Theory]
    [MemberData(nameof(Forms))]
    public async Task TheUploadFormMustHoldExactly1File(TestDatabase database, string form, HttpStatusCode status)
    {
        var api = await hosts.OnAsync(database);

        using var response = await api.SendAsync(
            HttpMethod.Post, FilesApi.Route + FilesApi.OwnerQuery("customer-1"), Callers.OnboardingSvc, Body(form), FilesApi.NewKey());

        await response.ShouldHaveStatusAsync(status);
        (await api.FileNamesAsync("customer-1")).ShouldBeEmpty();
    }

    public static IEnumerable<object[]> BadKeys => Databases.With(
        ["no idempotency key"],
        ["the key \"upload 1\""],
        ["a key of 256 characters"]);

    /// <summary>Scenario Outline: The upload needs a well-formed idempotency key.</summary>
    [Theory]
    [MemberData(nameof(BadKeys))]
    public async Task TheUploadNeedsAWellFormedIdempotencyKey(TestDatabase database, string key)
    {
        var api = await hosts.OnAsync(database);
        var sent = key switch
        {
            "no idempotency key" => null,
            "the key \"upload 1\"" => "upload 1",
            "a key of 256 characters" => new string('k', 256),
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, null)
        };

        using var response = await api.UploadAsync(Callers.OnboardingSvc, "customer-1", "passport.pdf", Bytes.Of(1024), sent);

        await response.ShouldBeProblemDetailsAsync(HttpStatusCode.BadRequest);
    }

    /// <summary>Scenario: The bytes are stored under a key built from the file id.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task TheBytesAreStoredUnderAKeyBuiltFromTheFileId(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var bytes = Bytes.Of(1024);

        using var response = await api.UploadAsync(Callers.OnboardingSvc, "customer-1", "Passport.PDF", bytes, FilesApi.NewKey());

        await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
        var fileId = (await response.JsonAsync()).GetProperty("fileId").GetGuid();
        api.StoredBlobs().ShouldBe([$"files/{fileId}.pdf"]);
        (await File.ReadAllBytesAsync(Path.Combine(api.Host.BlobRoot, "files", $"{fileId}.pdf"))).ShouldBe(bytes);
        api.StoredBlobs().ShouldAllBe(key => !key.Contains("customer-1") && !key.Contains("Passport"));
    }

    /// <summary>The path of the answer's <c>Location</c> header, without its query.</summary>
    internal static string? LocationPath(HttpResponseMessage response) =>
        response.Headers.Location is not { } location
            ? null
            : location.IsAbsoluteUri ? location.AbsolutePath : location.OriginalString.Split('?')[0];

    private static HttpContent Body(string form)
    {
        switch (form)
        {
            case "a JSON body instead of a form":
                return JsonContent.Create(new { fileName = "passport.pdf" });
            case "a form with no file part":
                return new MultipartFormDataContent();
            case "a form with 2 file parts":
                return FilesApi.Form(
                    ("passport.pdf", Bytes.Of(1024), "application/pdf"),
                    ("visa.pdf", Bytes.Of(1024), "application/pdf"));
            case "a form with \"passport.pdf\" and a group id":
                var withGroup = FilesApi.Form(("passport.pdf", Bytes.Of(1024), "application/pdf"));
                withGroup.Add(new StringContent(Guid.NewGuid().ToString()), "groupId");
                return withGroup;
            default:
                throw new ArgumentOutOfRangeException(nameof(form), form, null);
        }
    }
}
