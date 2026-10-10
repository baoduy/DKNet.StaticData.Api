using System.Text;

namespace DKNet.StaticData.App.Tests.Client;

/// <summary>
/// Stands in for the network: records the last request it saw and answers with a canned response, so a client
/// scenario never needs the service. Used as a typed client's primary transport (<c>ConfigurePrimaryHttpMessageHandler</c>).
/// </summary>
public sealed class RecordingHandler : DelegatingHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }

    public string ResponseBody { get; set; } = "{}";

    public HttpStatusCode ResponseStatusCode { get; set; } = HttpStatusCode.OK;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        return Task.FromResult(new HttpResponseMessage(ResponseStatusCode)
        {
            Content = new StringContent(ResponseBody, Encoding.UTF8, "application/json")
        });
    }
}
