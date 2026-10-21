using DKNet.StaticData.Api.Configs.AzureAppConfig;
using Microsoft.Extensions.Configuration;

namespace DKNet.StaticData.App.Tests.Unit.Configs;

/// <summary>
///     DRK-2351: the label selector also fetches the feature flags that carry the service label, so every setting read
///     from the store goes through <see cref="AzureAppConfigSettings.ShouldLoadSetting" />, which keeps flags only as
///     <c>LoadFeatureFlags</c> and <c>FeatureFlagPrefix</c> allow and always keeps plain keys.
/// </summary>
public class AzureAppConfigSettingFilterTests
{
    private const string StaticDataFlag = ".appconfig.featureflag/StaticData.NewUi";
    private const string BillingFlag = ".appconfig.featureflag/Billing.NewUi";
    private const string PlainKey = "Logging:LogLevel:Default";

    [Fact]
    public void FlagWithTheServiceLabelIsDroppedWhenLoadFeatureFlagsIsFalse()
    {
        Settings(false, "").ShouldLoadSetting(StaticDataFlag).ShouldBeFalse();
    }

    [Fact]
    public void FlagOutsideFeatureFlagPrefixIsDroppedWhateverItsLabel()
    {
        Settings(true, "StaticData.").ShouldLoadSetting(BillingFlag).ShouldBeFalse();
    }

    [Theory]
    [InlineData(false, "")]
    [InlineData(false, "StaticData.")]
    [InlineData(true, "StaticData.")]
    [InlineData(true, "")]
    public void PlainKeysKeepLoadingWhateverTheFlagSettings(bool loadFeatureFlags, string prefix)
    {
        Settings(loadFeatureFlags, prefix).ShouldLoadSetting(PlainKey).ShouldBeTrue();
    }

    [Theory]
    [InlineData(false, "StaticData.", StaticDataFlag, false)]
    [InlineData(true, "StaticData.", ".appconfig.featureflag/staticdata.NewUi", false)]
    [InlineData(true, "StaticData.", StaticDataFlag, true)]
    [InlineData(true, "", StaticDataFlag, true)]
    [InlineData(false, "", "Settings/.appconfig.featureflag/NewUi", true)]
    public void FlagIsKeptOnlyWhenFlagsAreOnAndItsNameStartsWithThePrefix(bool loadFeatureFlags, string prefix,
        string key, bool expected)
    {
        Settings(loadFeatureFlags, prefix).ShouldLoadSetting(key).ShouldBe(expected);
    }

    [Fact]
    public void WithNoSectionEveryFlagIsKept()
    {
        var settings = AzureAppConfigSettings.Resolve(new ConfigurationBuilder().Build());

        settings.ShouldLoadSetting(StaticDataFlag).ShouldBeTrue();
    }

    private static AzureAppConfigSettings Settings(bool loadFeatureFlags, string prefix) =>
        new("AzureAppConfig", "DKNet.StaticData.Api", loadFeatureFlags, prefix, TimeSpan.FromSeconds(300));
}
