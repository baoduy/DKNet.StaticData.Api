using DKNet.StaticData.App.TestSupport;

namespace DKNet.StaticData.App.BDDTests.Support;

public sealed class BddApiFactory() : TestApiFactoryBase("bdd-tests")
{
    protected override void AddFeatureOverrides(IDictionary<string, string?> settings) =>
        settings["FeatureManagement:RequireAuthorization"] = "false";
}
