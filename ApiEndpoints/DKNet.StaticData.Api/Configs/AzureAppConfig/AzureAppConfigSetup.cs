using Azure.Identity;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;

namespace DKNet.StaticData.Api.Configs.AzureAppConfig;

/// <summary>
///     Extension methods for configuring Azure App Configuration integration
/// </summary>
[ExcludeFromCodeCoverage]
internal static class AzureAppConfigSetup
{
    #region Methods

    /// <summary>
    ///     Adds Azure App Configuration as a configuration source
    /// </summary>
    /// <param name="builder">The configuration builder</param>
    /// <param name="feature">The feature flag management</param>
    /// <returns>The configuration builder</returns>
    public static WebApplicationBuilder AddAzureAppConfig(this WebApplicationBuilder builder, FeatureOptions feature)
    {
        if (!feature.EnableAzureAppConfig)
        {
            return builder;
        }

        var settings = AzureAppConfigSettings.Resolve(builder.Configuration);
        var conn = builder.Configuration.GetConnectionString(settings.ConnectionStringName);
        if (string.IsNullOrWhiteSpace(conn))
        {
            return builder;
        }

        builder.Configuration.AddAzureAppConfiguration(op =>
        {
            op.Connect(new Uri(conn), new DefaultAzureCredential())
                .ConfigureRefresh(c => c.RegisterAll().SetRefreshInterval(settings.RefreshInterval));

            if (settings.LoadFeatureFlags)
            {
                if (string.IsNullOrEmpty(settings.FeatureFlagPrefix))
                {
                    op.UseFeatureFlags();
                }
                else
                {
                    op.UseFeatureFlags(ff => ff.Select(settings.FeatureFlagPrefix + "*"));
                }
            }

            op.Select(KeyFilter.Any, settings.Label);
            // The label selector also fetches the feature flags carrying the label; drop those the flag settings exclude.
            op.Map(setting => ValueTask.FromResult(settings.ShouldLoadSetting(setting.Key) ? setting : null));
        });

        builder.Services.MarkFeatureAdded(nameof(AzureAppConfigSetup));

        return builder;
    }

    /// <summary>
    ///     Announces Azure App Configuration through <c>ILogger</c> once a host (and its logging pipeline) exists —
    ///     the source is already read at builder time in <see cref="AddAzureAppConfig" />, before any logger is
    ///     available.
    /// </summary>
    public static WebApplication UseAzureAppConfig(this WebApplication app)
    {
        if (app.Services.IsFeatureAdded(nameof(AzureAppConfigSetup)))
        {
            app.Logger.LogInformation("{Feature} enabled", nameof(AzureAppConfigSetup));
        }

        return app;
    }

    #endregion
}