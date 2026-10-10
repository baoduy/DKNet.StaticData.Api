using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

namespace DKNet.StaticData.Api.Configs.Auth;

/// <summary>
///     Provides extension methods for configuring authentication and authorization in an ASP.NET Core application.
/// </summary>
[ExcludeFromCodeCoverage]
internal static class AuthConfig
{
    #region Fields

    /// <summary>The app role, and the policy of the same name, every <c>GET</c> under <c>/v1</c> needs (ADR-0016).</summary>
    public const string ReadRole = "staticdata.read";

    /// <summary>The app role, and the policy of the same name, every <c>POST</c>, <c>PUT</c> and <c>DELETE</c> needs.</summary>
    public const string WriteRole = "staticdata.write";

    /// <summary>The only claim app roles are read from: a scope (<c>scp</c>) never grants one.</summary>
    private const string RolesClaim = "roles";

    #endregion

    #region Methods

    /// <summary>
    ///     Adds authentication and authorization services to the specified <see cref="IServiceCollection" />.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection" /> to add the authentication and authorization services to.</param>
    /// <returns>The updated <see cref="IServiceCollection" /> instance.</returns>
    /// <remarks>
    ///     This method configures the application to use JWT (JSON Web Token) Bearer authentication.
    ///     The token signature is validated against the issuer metadata from the
    ///     <c>Authentication:Schemes:Bearer:MetadataAddress</c> configuration.
    /// </remarks>
    public static IServiceCollection AddAuthConfig(this IServiceCollection services)
    {
        services.MarkConfigAdded(nameof(AuthConfig));

        services.AddAuthentication()
            .AddJwtBearer(options =>
            {
                // Claims keep their token names, so "roles", "client_id", "azp" and "appid" are read as issued.
                options.MapInboundClaims = false;
                options.Events = new JwtBearerEvents
                {
                    // A valid token without a caller id is not a caller: 401, not 403 (ADR-0016).
                    OnTokenValidated = context =>
                    {
                        if (CallerAccessor.Read(context.Principal) is null)
                        {
                            context.Fail("The token carries no caller id (client_id, azp or appid).");
                        }

                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorization(options =>
        {
            // Default deny: any endpoint not explicitly declared anonymous requires an authenticated caller.
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
            options.AddPolicy(ReadRole, policy => policy.RequireAuthenticatedUser().RequireClaim(RolesClaim, ReadRole));
            options.AddPolicy(WriteRole, policy => policy.RequireAuthenticatedUser().RequireClaim(RolesClaim, WriteRole));
        });
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, ProblemDetailsForbiddenHandler>();

        return services;
    }

    /// <summary>
    ///     Configures the specified <see cref="WebApplication" /> to use the added authentication and authorization services.
    /// </summary>
    /// <param name="app">The <see cref="WebApplication" /> to configure.</param>
    /// <returns>The updated <see cref="WebApplication" /> instance.</returns>
    /// <remarks>
    ///     This method enables authentication and authorization middleware only if the authentication configuration has been
    ///     added to the services.
    /// </remarks>
    public static WebApplication UseAuthConfig(this WebApplication app)
    {
        if (app.Services.IsConfigAdded(nameof(AuthConfig)))
        {
            app.UseAuthentication();
            app.UseAuthorization();
            app.Logger.LogInformation(nameof(AuthConfig) + " enabled");
        }

        if (app.Services.IsConfigAdded(nameof(DemoAuthConfig)))
        {
            app.UseAuthentication();
            app.Logger.LogInformation(nameof(DemoAuthConfig) + " enabled");
        }

        return app;
    }

    #endregion
}