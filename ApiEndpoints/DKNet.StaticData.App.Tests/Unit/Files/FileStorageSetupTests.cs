using DKNet.StaticData.Infra.Extensions;
using DKNet.StaticData.Share.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace DKNet.StaticData.App.Tests.Unit.Files;

/// <summary>
/// Brief DRK-2209 §6a D8: an operator's allow-list replaces the default one (never appends to it), and is kept in
/// lowercase, so a <c>.PDF</c> entry matches a <c>.pdf</c> file.
/// </summary>
public class FileStorageSetupTests
{
    [Fact]
    public void AnOperatorsAllowListReplacesTheDefaultInLowercase()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Files:AllowedExtensions:0"] = ".PDF",
                ["Files:AllowedExtensions:1"] = " .Png "
            })
            .Build();
        using var services = new ServiceCollection().AddLogging().AddFileStorage(configuration).BuildServiceProvider();

        services.GetRequiredService<IOptions<FileSettings>>().Value.AllowedExtensions.ShouldBe([".pdf", ".png"]);
    }
}
