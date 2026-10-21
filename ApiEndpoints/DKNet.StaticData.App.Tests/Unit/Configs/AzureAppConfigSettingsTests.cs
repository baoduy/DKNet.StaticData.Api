using DKNet.StaticData.Api.Configs.AzureAppConfig;
using Microsoft.Extensions.Configuration;

namespace DKNet.StaticData.App.Tests.Unit.Configs;

/// <summary>
///     DRK-2336: the service applies the Azure App Configuration options set under the <c>AzureAppConfiguration</c>
///     section, and the connection string stays <c>ConnectionStrings:AzureAppConfig</c>.
/// </summary>
public class AzureAppConfigSettingsTests
{
    [Fact]
    public void OptionsSetUnderAzureAppConfigurationAreRead()
    {
        var settings = Resolve(
            ("AzureAppConfiguration:Label", "prod"),
            ("AzureAppConfiguration:LoadFeatureFlags", "false"),
            ("AzureAppConfiguration:FeatureFlagPrefix", "StaticData."),
            ("AzureAppConfiguration:CacheExpirationInSeconds", "60"));

        settings.ShouldBe(new AzureAppConfigSettings("AzureAppConfig", "prod", false, "StaticData.",
            TimeSpan.FromSeconds(60)));
    }

    [Fact]
    public void OptionsSetOnlyUnderTheOldAzureAppConfigSectionAreIgnored()
    {
        var settings = Resolve(
            ("AzureAppConfig:Label", "prod"),
            ("AzureAppConfig:LoadFeatureFlags", "false"),
            ("AzureAppConfig:FeatureFlagPrefix", "StaticData."),
            ("AzureAppConfig:ConnectionStringName", "Other"));

        settings.ShouldBe(new AzureAppConfigSettings("AzureAppConfig", "DKNet.StaticData.Api", true, string.Empty,
            TimeSpan.FromSeconds(300)));
    }

    [Fact]
    public void WithNoSectionTheDefaultsApply()
    {
        var settings = Resolve();

        settings.ShouldBe(new AzureAppConfigSettings("AzureAppConfig", "DKNet.StaticData.Api", true, string.Empty,
            TimeSpan.FromSeconds(300)));
    }

    // The same label is set under the old section too: today that is the section read, so the reproduction
    // shows an empty label passing through instead of selecting the service name.
    [Theory]
    [InlineData(null, "DKNet.StaticData.Api")]
    [InlineData("", "DKNet.StaticData.Api")]
    [InlineData("  ", "DKNet.StaticData.Api")]
    [InlineData("prod", "prod")]
    public void AnEmptyLabelSelectsTheServiceName(string? label, string expected)
    {
        var settings = Resolve(("AzureAppConfiguration:Label", label), ("AzureAppConfig:Label", label));

        settings.Label.ShouldBe(expected);
    }

    [Theory]
    [InlineData("false", false)]
    [InlineData("true", true)]
    [InlineData(null, true)]
    public void FeatureFlagsLoadUnlessLoadFeatureFlagsIsFalse(string? loadFeatureFlags, bool expected)
    {
        var settings = Resolve(("AzureAppConfiguration:LoadFeatureFlags", loadFeatureFlags));

        settings.LoadFeatureFlags.ShouldBe(expected);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("StaticData.", "StaticData.")]
    public void FeatureFlagPrefixIsAppliedOnlyWhenSet(string? prefix, string expected)
    {
        var settings = Resolve(("AzureAppConfiguration:FeatureFlagPrefix", prefix));

        settings.FeatureFlagPrefix.ShouldBe(expected);
    }

    [Theory]
    [InlineData(null, 300)]
    [InlineData("300", 300)]
    [InlineData("60", 60)]
    [InlineData("1", 1)]
    [InlineData("0", 300)]
    [InlineData("-5", 300)]
    public void RefreshFollowsCacheExpirationInSeconds(string? cacheExpirationInSeconds, int expectedSeconds)
    {
        var settings = Resolve(("AzureAppConfiguration:CacheExpirationInSeconds", cacheExpirationInSeconds));

        settings.RefreshInterval.ShouldBe(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Fact]
    public void TheConnectionStringNameIsNotTheSectionName()
    {
        var settings = Resolve(("AzureAppConfiguration:Label", "prod"));

        settings.ConnectionStringName.ShouldBe("AzureAppConfig");
    }

    private static AzureAppConfigSettings Resolve(params (string Key, string? Value)[] values) =>
        AzureAppConfigSettings.Resolve(new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build());
}
