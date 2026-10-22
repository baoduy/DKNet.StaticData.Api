using DKNet.StaticData.Api.Configs.Auth;
using DKNet.StaticData.Api.Configs.AzureAppConfig;
using DKNet.AspCore.Extensions.Antiforgery;
using DKNet.AspCore.Extensions.RateLimits;
using DKNet.StaticData.Api.Configs.Swagger;

namespace DKNet.StaticData.Api.Configs;

[ExcludeFromCodeCoverage]
internal static class AppConfig
{
    #region Methods

    public static IServiceCollection AddAppConfig(
        this IServiceCollection services,
        FeatureOptions features,
        IConfiguration configuration,
        DatabaseProvider database)
    {
        if (features.EnableAntiforgery)
        {
            services.AddAntiforgeryConfig();
        }

        if (features.RequireAuthorization && features.EnableDemoAuthentication)
        {
            throw new InvalidOperationException(
                $"{nameof(FeatureOptions.RequireAuthorization)} and {nameof(FeatureOptions.EnableDemoAuthentication)} " +
                "cannot both be enabled: the demonstration identity is never a real caller.");
        }

        if (features.RequireAuthorization)
        {
            services.AddAuthConfig();
        }
        else if (features.EnableDemoAuthentication)
        {
            services.AddDemoAuthConfig();
        }

        if (features.EnableSwagger)
        {
            services.AddOpenApiDoc();
        }

        if (features.EnableHttps)
        {
            services.AddHttpsConfig(configuration);
        }

        if (features.EnableRateLimit)
        {
            services.AddRateLimitConfig(configuration);
        }

        if (features.EnableVersioning)
        {
            services.AddAppVersioning();
        }

        if (features.EnableForwardedHeaders)
        {
            services.AddForwardedHeadersConfig(configuration);
        }

        services.AddSecurityHeadersConfig(features);

        if (features.EnableRequestBounds)
        {
            services.AddRequestBoundsConfig(configuration);
        }

        services.AddHttpContextAccessor()
            .AddFeatureManagement();

        services.AddIdempotencyConfig(configuration, database)
            .AddFileStorage(configuration);

        return services
            .AddCorsConfig(configuration)
            .AddAllAppServices(database)
            .AddHealthzConfig(features);
    }

    public static Task UseAppConfig(this WebApplication app, Action<WebApplication>? extra = null)
    {
        // Forwarded headers and security headers run first: forwarded headers must rewrite RemoteIpAddress
        // before anything (CORS, rate limiting) makes a decision based on it, and security headers must wrap
        // everything downstream, including the global exception handler, for 200/404/500 responses alike (R5).
        app.UseAzureAppConfig()
            .UseForwardedHeadersConfig()
            .UseSecurityHeadersConfig()
            .UseCorsConfig()
            .UseHttpsConfig()
            .UseHealthzConfig();

        // An error answer that carries no body of its own — the bearer challenge's 401, a 404 for an address the
        // service does not serve, the request timeout's 504 — still answers as problem details (spec DRK-2206 §3).
        app.UseStatusCodePages();
        app.UseRouting();

        // After UseRouting, so the antiforgery guard can see whether the endpoint turns the check off.
        app.UseAntiforgeryConfig();
        app.UseRequestBoundsConfig();

        // Before authentication on purpose (spec DRK-2366 R2): a caller is limited by client address, so moving it
        // after UseAuthConfig would change who shares a bucket.
        app.UseRateLimitConfig();

        //This must be after UseRouting
        app.UseAuthConfig();

        //This is UseEndpoints
        extra?.Invoke(app);

        //These have to be after UseEndpoints.
        app.UseOpenApiDoc();

        return app.RunAsync();
    }

    #endregion
}