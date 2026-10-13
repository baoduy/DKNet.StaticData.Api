using System.Text;

namespace DKNet.StaticData.App.Tests.Client;

/// <summary>
/// Stands in for the network: records every request it saw and answers each with a canned response, so a client
/// scenario never needs the service. Used as a typed client's primary transport (<c>ConfigurePrimaryHttpMessageHandler</c>).
/// </summary>
public sealed class RecordingHandler : DelegatingHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }

    /// <summary>Every request it saw, in order.</summary>
    public List<HttpRequestMessage> Requests { get; } = [];

    /// <summary>The last request's body, read before the client disposes it; <see langword="null"/> when it had none.</summary>
    public string? LastBody { get; private set; }

    public string ResponseBody { get; set; } = "{}";

    public HttpStatusCode ResponseStatusCode { get; set; } = HttpStatusCode.OK;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        Requests.Add(request);
        LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        return new HttpResponseMessage(ResponseStatusCode)
        {
            Content = new StringContent(ResponseBody, Encoding.UTF8, "application/json")
        };
    }
}
