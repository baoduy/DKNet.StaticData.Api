using OwaspHeaders.Core;
using OwaspHeaders.Core.Enums;
using OwaspHeaders.Core.Extensions;
using OwaspHeaders.Core.Helpers;
using OwaspHeaders.Core.Models;
using DKNet.StaticData.Api.Configs.Swagger;

namespace DKNet.StaticData.Api.Configs;

[ExcludeFromCodeCoverage]
internal static class SecurityHeadersConfig
{
    #region Methods

    public static IServiceCollection AddSecurityHeadersConfig(this IServiceCollection services, FeatureOptions features)
    {
        if (!features.EnableSecurityHeaders)
        {
            return services;
        }

        services.MarkFeatureAdded(nameof(SecurityHeadersConfig));
        return services;
    }

    /// <summary>
    ///     OwaspHeaders.Core's middleware writes its headers straight onto <c>HttpContext.Response.Headers</c>
    ///     before calling the next delegate. <c>UseExceptionHandler</c>'s <c>IExceptionHandler</c> path calls
    ///     <c>Response.Clear()</c> (which empties Headers) before writing the problem response, so headers added
    ///     the normal way never reach an unhandled-exception response. Deferring the same header-write call to
    ///     <c>HttpResponse.OnStarting</c> runs it right before the response actually sends — after any such
    ///     clear — so success, 404 and unhandled-500 responses all carry the headers (R5).
    /// </summary>
    public static WebApplication UseSecurityHeadersConfig(this WebApplication app)
    {
        if (!app.Services.IsFeatureAdded(nameof(SecurityHeadersConfig)))
        {
            return app;
        }

        var headerWriter = CreateWriter(StrictConfig());

        // Scalar's documentation page passes the OpenAPI document's URL to its bundle from an inline
        // <script type="module">, which script-src 'self' blocks: the page then loads but never calls
        // initialize(), rendering empty without ever requesting /openapi/v1.json. Only this one path gets
        // 'unsafe-inline' back, and only while the documentation is actually mapped — every other response,
        // the OpenAPI document included, keeps the strict policy.
        var docsHeaderWriter = app.Services.IsFeatureAdded(nameof(SwaggerConfig))
            ? CreateWriter(DocsConfig())
            : null;

        app.Use(async (context, next) =>
        {
            var writer = docsHeaderWriter is not null &&
                         context.Request.Path.StartsWithSegments(SwaggerConfig.DocsPath,
                             StringComparison.OrdinalIgnoreCase)
                ? docsHeaderWriter
                : headerWriter;

            context.Response.OnStarting(() => writer.InvokeAsync(context));
            await next();
        });

        app.Logger.LogInformation("{Feature} enabled", nameof(SecurityHeadersConfig));
        return app;
    }

    private static SecureHeadersMiddleware CreateWriter(SecureHeadersMiddlewareConfiguration config) =>
        new(_ => Task.CompletedTask, config);

    private static SecureHeadersMiddlewareConfiguration StrictConfig() =>
        BaseConfig().UseDefaultContentSecurityPolicy().Build();

    // Mirrors UseDefaultContentSecurityPolicy()'s script-src/object-src 'self', with 'unsafe-inline'
    // added to script-src for Scalar's inline bootstrap.
    private static SecureHeadersMiddlewareConfiguration DocsConfig() =>
        BaseConfig()
            .UseContentSecurityPolicy()
            .SetCspUris(
            [
                ContentSecurityPolicyHelpers.CreateSelfDirective(),
                new ContentSecurityPolicyElement
                {
                    CommandType = CspCommandType.Directive,
                    DirectiveOrUri = "unsafe-inline"
                }
            ], CspUriType.Script)
            .SetCspUris([ContentSecurityPolicyHelpers.CreateSelfDirective()], CspUriType.Object)
            .Build();

    // HttpsConfig (HttpsConfig.cs) owns Strict-Transport-Security — UseHsts() deliberately not called here
    // to avoid emitting it twice.
    private static SecureHeadersBuilder BaseConfig() =>
        new SecureHeadersBuilder()
            .UseXFrameOptions()
            .UseContentTypeOptions()
            .UsePermittedCrossDomainPolicies()
            .UseReferrerPolicy()
            .UseCacheControl()
            .UseXssProtection()
            .UseCrossOriginResourcePolicy();

    #endregion
}
