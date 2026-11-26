using DKNet.StaticData.App.TestSupport;

namespace DKNet.StaticData.App.Tests.Integration.Support;

public sealed class ApiFixture : TestApiFactoryBase, IAsyncLifetime
{
    #region Methods

    public async ValueTask InitializeAsync()
    {
        _ = CreateClient();
        await ResetDatabaseAsync();
    }

    #endregion
}
