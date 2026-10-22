using DKNet.StaticData.App.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;

namespace DKNet.StaticData.App.Tests.Integration.Hosting;

/// <summary>
/// The service started with the hosting settings a scenario names — feature flags, <c>Security</c>, <c>Cors</c>,
/// <c>RateLimit</c>, <c>RequestBounds</c> — on top of its shipped <c>appsettings.json</c> and
/// <c>appsettings.Testing.json</c>. The settings arrive as environment variables through <see cref="TestHost"/>,
/// because <c>Program.cs</c> reads them while it registers services, before configuration added through the factory
/// is merged; no settings file is edited. Runs on TestServer, or on real Kestrel where TestServer cannot show the
/// behaviour (Kestrel's request body limit).
/// </summary>
public sealed class HostingHost : TestApiFactoryBase
{
    private HostingHost(bool kestrel)
    {
        if (kestrel)
        {
            UseKestrel(0);
        }
    }

    /// <summary>
    /// Builds and starts the service with <paramref name="settings"/> (environment-variable keys, e.g.
    /// <c>FeatureManagement__EnableRateLimit</c>). Throws what the service's start-up threw.
    /// </summary>
    public static async Task<HostingHost> StartAsync(
        IReadOnlyDictionary<string, string?> settings,
        bool kestrel = false)
    {
        var (host, error) = await TestHost.StartAsync(new HostingHost(kestrel), settings);
        return host ?? throw new InvalidOperationException("The service did not start.", error);
    }

    /// <summary>
    /// Makes the service's own bearer scheme trust <see cref="TestTokens"/> for a scenario that turns token checking
    /// on; with it off, the bearer scheme is not registered and this changes nothing.
    /// </summary>
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        TestTokens.TrustTestIssuer(services);
    }

    /// <summary>
    /// Sends one request through TestServer with full control of the connection — the only way to set the peer
    /// address (<see cref="ConnectionInfo.RemoteIpAddress"/>), which TestServer otherwise leaves null. Returns the
    /// request's context once the service has answered, so the client address and scheme the service saw can be read
    /// from it.
    /// </summary>
    public Task<HttpContext> SendAsync(
        string method,
        string path,
        string? peer = null,
        string host = "localhost",
        string scheme = "http",
        IReadOnlyDictionary<string, string>? headers = null) =>
        Server.SendAsync(ctx =>
        {
            ctx.Request.Method = method;
            ctx.Request.Scheme = scheme;
            ctx.Request.Host = new HostString(host);
            ctx.Request.Path = path;
            if (peer is not null)
            {
                ctx.Connection.RemoteIpAddress = IPAddress.Parse(peer);
            }

            foreach (var (name, value) in headers ?? new Dictionary<string, string>())
            {
                ctx.Request.Headers[name] = value;
            }
        });
}
