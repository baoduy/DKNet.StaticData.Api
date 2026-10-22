using DKNet.StaticData.Api.Configs.AzureAppConfig;

namespace DKNet.StaticData.App.Tests.Unit.Configs;

/// <summary>
///     DRK-2351 R4: a whitespace <c>FeatureFlagPrefix</c> is not empty, so the flag selector selects <c>" *"</c> and
///     the setting filter must drop every flag whose name does not start with it, never treat it as "no prefix".
/// </summary>
public class AzureAppConfigSettingPrefixTests
{
    [Fact]
    public void WhitespacePrefixIsNotTreatedAsEmpty()
    {
        var settings = new AzureAppConfigSettings("AzureAppConfig", "DKNet.StaticData.Api", true, " ",
            TimeSpan.FromSeconds(300));

        settings.ShouldLoadSetting(".appconfig.featureflag/StaticData.NewUi").ShouldBeFalse();
    }
}
