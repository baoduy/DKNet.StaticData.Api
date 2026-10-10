using System.Net.Http.Headers;
using System.Text;
using DKNet.StaticData.App.Tests.Client;
using DKNet.StaticData.Client;

namespace DKNet.StaticData.App.Tests.Unit.Client;

/// <summary>
/// The typed client's wire rules the client scenarios leave open (brief DRK-2210 §6a): the owner's URL encoding, the
/// list query, the 3 kinds of error body, a missing answer field and the download's name and content type.
/// </summary>
public sealed class StaticDataFileClientTests
{
    private static readonly Guid FileId = Guid.Parse("0d6f0c2a-5b8e-4f43-9a51-3f0f4c8f2b11");

    private static StaticDataFileClient ClientOver(HttpMessageHandler network) =>
        new(new HttpClient(network) { BaseAddress = new Uri("https://staticdata.example.test") });

    [Fact]
    public async Task AnUploadSendsTheKeyHeaderAndOneFilePart()
    {
        HttpRequestMessage? sent = null;
        string[] parts = [];
        var client = ClientOver(new AnswerHandler(request =>
        {
            sent = request;
            // read inside the send: the client disposes the form once the call returns
            parts = [.. ((MultipartFormDataContent)request.Content!).Select(part =>
                $"{part.Headers.ContentDisposition!.Name}|{part.Headers.ContentDisposition.FileName}|{part.ReadAsStringAsync().Result}")];
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent($$"""{"fileId":"{{FileId}}","owner":"a b&c","fileName":"passport.pdf"}""", Encoding.UTF8, "application/json")
            };
        }));

        var file = await client.UploadAsync("a b&c", "upload-1", "passport.pdf", new MemoryStream("%PDF-1.7"u8.ToArray()));

        sent.ShouldNotBeNull().Method.ShouldBe(HttpMethod.Post);
        sent.RequestUri.ShouldNotBeNull().AbsoluteUri.ShouldBe("https://staticdata.example.test/v1/files?owner=a%20b%26c");
        sent.Headers.GetValues("X-Idempotency-Key").ShouldBe(["upload-1"]);
        parts.ShouldBe(["file|passport.pdf|%PDF-1.7"]);
        file.FileId.ShouldBe(FileId);
        file.Owner.ShouldBe("a b&c");
    }

    [Fact]
    public async Task ADownloadAndADeleteCallTheirRoutes()
    {
        var network = new RecordingHandler();
        var client = ClientOver(network);

        await using (await client.DownloadAsync("customer-1", FileId))
        {
            network.LastRequest.ShouldNotBeNull().Method.ShouldBe(HttpMethod.Get);
            network.LastRequest.RequestUri.ShouldNotBeNull().AbsoluteUri
                .ShouldBe($"https://staticdata.example.test/v1/files/{FileId}/content?owner=customer-1");
        }

        await client.DeleteAsync("customer-1", FileId);

        network.LastRequest.ShouldNotBeNull().Method.ShouldBe(HttpMethod.Delete);
        network.LastRequest.RequestUri.ShouldNotBeNull().AbsoluteUri
            .ShouldBe($"https://staticdata.example.test/v1/files/{FileId}?owner=customer-1");
    }

    [Fact]
    public async Task TheRegistrationChainsTheApplicationsOwnHandlerAndTheBaseAddress()
    {
        var network = new RecordingHandler();
        var services = new ServiceCollection();
        services.AddTransient<MarkerHandler>();
        services.AddStaticDataClient(new Uri("https://staticdata.example.test"), typeof(MarkerHandler));
        services.AddHttpClient<IStaticDataFileClient, StaticDataFileClient>().ConfigurePrimaryHttpMessageHandler(() => network);
        await using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<IStaticDataFileClient>().GetAsync("customer-1", FileId);

        var request = network.LastRequest.ShouldNotBeNull();
        request.Headers.GetValues(MarkerHandler.Header).ShouldBe(["chained"]);
        request.RequestUri.ShouldNotBeNull().AbsoluteUri
            .ShouldBe($"https://staticdata.example.test/v1/files/{FileId}?owner=customer-1");
    }

    [Theory]
    [InlineData("customer-1", "owner=customer-1")]
    [InlineData("a b&c", "owner=a%20b%26c")]
    public async Task TheOwnerIsSentUrlEncoded(string owner, string query)
    {
        var network = new RecordingHandler();

        await ClientOver(network).GetAsync(owner, FileId);

        network.LastRequest.ShouldNotBeNull().RequestUri.ShouldNotBeNull().AbsoluteUri
            .ShouldBe($"https://staticdata.example.test/v1/files/{FileId}?{query}");
    }

    [Fact]
    public async Task AListSendsThePageAndTheDatesInvariantAndEncoded()
    {
        var network = new RecordingHandler();

        await ClientOver(network).ListAsync("customer-1", pageNumber: 2, pageSize: 5,
            fromDate: new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
            toDate: new DateTimeOffset(2026, 6, 30, 23, 59, 59, TimeSpan.FromHours(7)));

        network.LastRequest.ShouldNotBeNull().RequestUri.ShouldNotBeNull().AbsoluteUri.ShouldBe(
            "https://staticdata.example.test/v1/files?owner=customer-1&pageNumber=2&pageSize=5"
            + "&fromDate=2026-06-01T00%3A00%3A00.0000000%2B00%3A00&toDate=2026-06-30T23%3A59%3A59.0000000%2B07%3A00");
    }

    [Fact]
    public async Task AListWithoutDatesSendsOnlyTheOwnerAndPage()
    {
        var network = new RecordingHandler();

        await ClientOver(network).ListAsync("customer-1", pageNumber: 1, pageSize: 10);

        network.LastRequest.ShouldNotBeNull().RequestUri.ShouldNotBeNull().AbsoluteUri
            .ShouldBe("https://staticdata.example.test/v1/files?owner=customer-1&pageNumber=1&pageSize=10");
    }

    [Fact]
    public async Task AProblemDetailsErrorBecomesATypedErrorWithTheDetails()
    {
        var network = new RecordingHandler
        {
            ResponseStatusCode = HttpStatusCode.NotFound,
            ResponseBody = """{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.5","title":"Not Found","status":404,"detail":"No such file.","instance":"/v1/files"}"""
        };

        var error = await Should.ThrowAsync<StaticDataApiException>(() => ClientOver(network).GetAsync("customer-1", FileId));

        error.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        error.Message.ShouldBe("The StaticData service answered 404 Not Found.");
        var details = error.ProblemDetails.ShouldNotBeNull();
        details.Type.ShouldBe("https://tools.ietf.org/html/rfc9110#section-15.5.5");
        details.Title.ShouldBe("Not Found");
        details.Status.ShouldBe(404);
        details.Detail.ShouldBe("No such file.");
        details.Instance.ShouldBe("/v1/files");
    }

    [Theory]
    [InlineData("""{"title":"Conflict"}""")]
    [InlineData("""{"type":"about:blank"}""")]
    public async Task AProblemDetailsBodyWithoutAStatusStillCarriesItsDetails(string body)
    {
        var network = new RecordingHandler { ResponseStatusCode = HttpStatusCode.Conflict, ResponseBody = body };

        var error = await Should.ThrowAsync<StaticDataApiException>(() => ClientOver(network).DeleteAsync("customer-1", FileId));

        error.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        error.ProblemDetails.ShouldNotBeNull().Status.ShouldBeNull();
    }

    [Theory]
    [InlineData("""{"error":"no such file"}""")] // JSON, but not problem details
    [InlineData("no such file")] // not JSON
    [InlineData("")] // empty body
    public async Task AnErrorWithoutProblemDetailsBecomesATypedErrorWithNoDetails(string body)
    {
        var network = new RecordingHandler { ResponseStatusCode = HttpStatusCode.NotFound, ResponseBody = body };

        var error = await Should.ThrowAsync<StaticDataApiException>(() => ClientOver(network).GetAsync("customer-1", FileId));

        error.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        error.ProblemDetails.ShouldBeNull();
    }

    [Fact]
    public async Task AMissingAnswerFieldReadsAsEmpty()
    {
        var network = new RecordingHandler
        {
            ResponseBody = $$"""{"fileId":"{{FileId}}","fileName":"passport.pdf","version":1}"""
        };

        var file = await ClientOver(network).GetAsync("customer-1", FileId);

        file.FileId.ShouldBe(FileId);
        file.FileName.ShouldBe("passport.pdf");
        file.UpdatedBy.ShouldBeNull();
        file.UpdatedOn.ShouldBeNull();
    }

    [Fact]
    public async Task AnUpdatedByFieldIsRead()
    {
        var network = new RecordingHandler
        {
            ResponseBody = $$"""{"fileId":"{{FileId}}","updatedBy":"report-svc","updatedOn":"2026-06-10T09:00:00+00:00"}"""
        };

        var file = await ClientOver(network).GetAsync("customer-1", FileId);

        file.UpdatedBy.ShouldBe("report-svc");
        file.UpdatedOn.ShouldBe(new DateTimeOffset(2026, 6, 10, 9, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task ANullAnswerReadsAsAnEmptyPage()
    {
        var network = new RecordingHandler { ResponseBody = "null" };

        var page = await ClientOver(network).ListAsync("customer-1", pageNumber: 1, pageSize: 10);

        page.Items.ShouldBeEmpty();
        page.TotalItemCount.ShouldBe(0);
    }

    [Theory]
    [InlineData("passport.pdf", "passport-ascii.pdf", "passport.pdf")] // filename* wins
    [InlineData(null, "\"passport.pdf\"", "passport.pdf")] // quoted fallback, unquoted
    [InlineData(null, null, "")] // a disposition without a name
    public async Task ADownloadReadsTheStoredName(string? fileNameStar, string? fileName, string expected)
    {
        var disposition = new ContentDispositionHeaderValue("attachment") { FileNameStar = fileNameStar, FileName = fileName };
        var client = ClientOver(Download(content => content.Headers.ContentDisposition = disposition));

        await using var downloaded = await client.DownloadAsync("customer-1", FileId);

        downloaded.FileName.ShouldBe(expected);
    }

    [Fact]
    public async Task ADownloadWithoutDispositionOrContentTypeReadsBothAsEmptyAndKeepsTheBytes()
    {
        var client = ClientOver(Download(content => content.Headers.ContentType = null));

        await using var downloaded = await client.DownloadAsync("customer-1", FileId);
        using var reader = new StreamReader(downloaded.Content, Encoding.UTF8);

        downloaded.FileName.ShouldBe(string.Empty);
        downloaded.ContentType.ShouldBe(string.Empty);
        (await reader.ReadToEndAsync()).ShouldBe("%PDF-1.7");
    }

    [Fact]
    public async Task ADownloadReadsTheStoredContentType()
    {
        var client = ClientOver(Download(content => content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf")));

        await using var downloaded = await client.DownloadAsync("customer-1", FileId);

        downloaded.ContentType.ShouldBe("application/pdf");
    }

    [Fact]
    public async Task AFailedDownloadBecomesATypedError()
    {
        var network = new RecordingHandler { ResponseStatusCode = HttpStatusCode.ServiceUnavailable, ResponseBody = """{"status":503}""" };

        var error = await Should.ThrowAsync<StaticDataApiException>(() => ClientOver(network).DownloadAsync("customer-1", FileId));

        error.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        error.ProblemDetails.ShouldNotBeNull().Status.ShouldBe(503);
    }

    [Fact]
    public void ANullHttpClientIsRefused() =>
        Should.Throw<ArgumentNullException>(() => new StaticDataFileClient(null!)).ParamName.ShouldBe("httpClient");

    /// <summary>Answers every request 200 with the bytes <c>%PDF-1.7</c>, its headers set by <paramref name="setHeaders"/>.</summary>
    private static AnswerHandler Download(Action<HttpContent> setHeaders) =>
        new(_ =>
        {
            var content = new ByteArrayContent("%PDF-1.7"u8.ToArray());
            setHeaders(content);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });

    /// <summary>Answers every request with what <paramref name="answer"/> returns for it.</summary>
    private sealed class AnswerHandler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(answer(request));
    }

    /// <summary>Stands in for the application's own credential handler: marks every request it sees.</summary>
    private sealed class MarkerHandler : DelegatingHandler
    {
        public const string Header = "X-Test-Handler";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.Add(Header, "chained");
            return base.SendAsync(request, cancellationToken);
        }
    }
}
