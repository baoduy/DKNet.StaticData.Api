namespace DKNet.StaticData.Api.Configs.AzureAppConfig;

/// <summary>
///     Configuration options for Azure App Configuration integration
/// </summary>
[ExcludeFromCodeCoverage]
internal sealed class AzureAppConfigOptions
{
    #region Properties

    /// <summary>
    ///     Whether to load feature flags from Azure App Configuration; null loads them
    /// </summary>
    public bool? LoadFeatureFlags { get; set; } = true;

    /// <summary>
    ///     Refresh interval for configuration values in seconds
    /// </summary>
    public int CacheExpirationInSeconds { get; set; } = 300;

    /// <summary>
    ///     Connection string for Azure App Configuration
    /// </summary>
    public string ConnectionStringName { get; set; } = "AzureAppConfig";

    /// <summary>
    ///     Feature flag prefix to filter feature flags (optional)
    /// </summary>
    public string? FeatureFlagPrefix { get; set; }

    /// <summary>
    ///     Configuration section name
    /// </summary>
    public static string Name => "AzureAppConfiguration";

    /// <summary>
    ///     Label to filter configuration values (optional)
    /// </summary>
    public string? Label { get; set; }

    #endregion
}