namespace DKNet.StaticData.Api.Configs.AzureAppConfig;

/// <summary>
///     The Azure App Configuration settings the service applies, resolved from configuration.
/// </summary>
internal sealed record AzureAppConfigSettings(
    string ConnectionStringName,
    string Label,
    bool LoadFeatureFlags,
    string FeatureFlagPrefix,
    TimeSpan RefreshInterval)
{
    #region Methods

    public static AzureAppConfigSettings Resolve(IConfiguration configuration)
    {
        var options = configuration.GetSection(AzureAppConfigOptions.Name).Get<AzureAppConfigOptions>() ??
                      new AzureAppConfigOptions();

        return new AzureAppConfigSettings(
            options.ConnectionStringName,
            options.Label ?? SharedConsts.ApiName,
            true,
            string.Empty,
            TimeSpan.FromMinutes(30));
    }

    #endregion
}
