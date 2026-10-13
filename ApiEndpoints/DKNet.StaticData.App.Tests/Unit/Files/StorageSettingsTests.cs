using DKNet.StaticData.Infra.Extensions;
using DKNet.StaticData.Share.Options;
using DKNet.Svc.BlobStorage.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace DKNet.StaticData.App.Tests.Unit.Files;

/// <summary>
/// Spec DRK-2206 §5 "Limits and settings": the operator's blob storage provider and file type allow-list, read when
/// the service starts (<see cref="FileStorageSetup.AddFileStorage"/>), with the setting names of design
/// <c>04-data.md</c> FileSettings. An empty allow-list is the key present with an empty value — what
/// <c>"AllowedExtensions": []</c> in a settings file gives.
/// </summary>
public class StorageSettingsTests
{
    private static readonly string LocalRoot = Path.Combine(Path.GetTempPath(), "staticdata-settings-tests");

    public static TheoryData<string?, string> Providers => new()
    {
        { "", "DKNet.Svc.BlobStorage.Local.LocalBlobService" },
        { "Local", "DKNet.Svc.BlobStorage.Local.LocalBlobService" },
        { "AzureStorage", "DKNet.Svc.BlobStorage.AzureStorage.AzureStorageBlobService" },
        { "AwsS3", "DKNet.Svc.BlobStorage.AwsS3.S3BlobService" },
        // brief DRK-2209 §6a D7: the setting left out, and the name in another case
        { null, "DKNet.Svc.BlobStorage.Local.LocalBlobService" },
        { "local", "DKNet.Svc.BlobStorage.Local.LocalBlobService" }
    };

    /// <summary>Scenario Outline: The operator's setting picks the blob storage provider.</summary>
    [Theory]
    [MemberData(nameof(Providers))]
    public void TheOperatorsSettingPicksTheBlobStorageProvider(string? setting, string provider)
    {
        using var service = Start(new Dictionary<string, string?> { ["BlobStorage:Provider"] = setting });

        service.GetRequiredService<IBlobService>().GetType().FullName.ShouldBe(provider);
    }

    public static TheoryData<string> WrongSettings => new()
    {
        "the blob storage provider to \"Dropbox\"",
        "an empty file type allow-list"
    };

    /// <summary>Scenario Outline: A wrong storage setting stops the service at start.</summary>
    [Theory]
    [MemberData(nameof(WrongSettings))]
    public void AWrongStorageSettingStopsTheServiceAtStart(string setting)
    {
        var settings = setting == "an empty file type allow-list"
            ? new Dictionary<string, string?> { ["Files:AllowedExtensions"] = "" }
            : new Dictionary<string, string?> { ["BlobStorage:Provider"] = "Dropbox" };

        var stopped = Should.Throw<Exception>(() =>
        {
            using var service = Start(settings);
            _ = service.GetRequiredService<IOptions<FileSettings>>().Value;
            _ = service.GetRequiredService<IBlobService>();
        });

        stopped.ShouldNotBeOfType<NotImplementedException>("the service must refuse the setting, not lack the code");
    }

    /// <summary>Scenario: The default allow-list holds the 14 design extensions.</summary>
    [Fact]
    public void TheDefaultAllowListHoldsThe14DesignExtensions()
    {
        using var service = Start([]);

        service.GetRequiredService<IOptions<FileSettings>>().Value.AllowedExtensions.ShouldBe(
            [".pdf", ".png", ".jpg", ".jpeg", ".gif", ".txt", ".csv", ".json", ".xml", ".doc", ".docx", ".xls", ".xlsx", ".zip"],
            ignoreOrder: true);
    }

    /// <summary>
    /// The services <see cref="FileStorageSetup.AddFileStorage"/> registers from <paramref name="settings"/>, on top of
    /// the settings each provider needs to be built (none of them connects until it is first used).
    /// </summary>
    private static ServiceProvider Start(Dictionary<string, string?> settings)
    {
        var all = new Dictionary<string, string?>
        {
            ["BlobStorage:LocalFolder:RootFolder"] = LocalRoot,
            ["BlobService:AzureStorage:ConnectionString"] = "UseDevelopmentStorage=true",
            ["BlobService:AzureStorage:ContainerName"] = "files",
            ["BlobService:S3:ConnectionString"] = "http://localhost:9000",
            ["BlobService:S3:BucketName"] = "files",
            ["BlobService:S3:AccessKey"] = "test-access-key",
            ["BlobService:S3:Secret"] = "test-secret",
            ["BlobService:S3:RegionEndpointName"] = "us-east-1",
            ["BlobService:S3:ForcePathStyle"] = "true"
        };
        foreach (var (key, value) in settings)
        {
            all[key] = value;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(all).Build();
        var services = new ServiceCollection().AddLogging();
        services.AddFileStorage(configuration);
        return services.BuildServiceProvider();
    }
}
