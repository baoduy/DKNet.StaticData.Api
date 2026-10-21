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
    private const int DefaultCacheExpirationInSeconds = 300;

    #region Methods

    /// <summary>
    ///     Reads the <c>AzureAppConfiguration</c> section. A null, empty or whitespace label selects the service name;
    ///     a missing or null <c>LoadFeatureFlags</c> loads flags; a missing or null <c>FeatureFlagPrefix</c> loads every
    ///     flag; a missing, 0 or negative <c>CacheExpirationInSeconds</c> refreshes every 300 seconds. .NET 10 binds an
    ///     explicit null value, so null is handled the same as absent.
    /// </summary>
    public static AzureAppConfigSettings Resolve(IConfiguration configuration)
    {
        var options = configuration.GetSection(AzureAppConfigOptions.Name).Get<AzureAppConfigOptions>() ??
                      new AzureAppConfigOptions();

        return new AzureAppConfigSettings(
            options.ConnectionStringName,
            string.IsNullOrWhiteSpace(options.Label) ? SharedConsts.ApiName : options.Label,
            options.LoadFeatureFlags ?? true,
            options.FeatureFlagPrefix ?? string.Empty,
            // The SDK rejects a refresh interval under 1 second, so 0 or negative falls back to the default.
            TimeSpan.FromSeconds(options.CacheExpirationInSeconds > 0
                ? options.CacheExpirationInSeconds
                : DefaultCacheExpirationInSeconds));
    }

    #endregion
}
