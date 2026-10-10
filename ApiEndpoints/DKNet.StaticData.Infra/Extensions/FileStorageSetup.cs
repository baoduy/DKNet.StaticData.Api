using DKNet.StaticData.Share.Options;
using DKNet.Svc.BlobStorage.AwsS3;

namespace DKNet.StaticData.Infra.Extensions;

/// <summary>
///     Registers the file settings and the blob storage provider the operator chose.
/// </summary>
public static class FileStorageSetup
{
    #region Fields

    private const string ProviderKey = "BlobStorage:Provider";

    #endregion

    #region Methods

    /// <summary>
    ///     Binds <see cref="FileSettings" /> from <c>Files</c> and registers the blob storage provider named by
    ///     <c>BlobStorage:Provider</c>.
    /// </summary>
    /// <param name="services">The service collection used to register dependencies.</param>
    /// <param name="configuration">The service's configuration.</param>
    /// <returns>The same <see cref="IServiceCollection" /> instance for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    ///     <c>Files:AllowedExtensions</c> is present but empty, or <c>BlobStorage:Provider</c> names no supported
    ///     provider: the service must not start.
    /// </exception>
    public static IServiceCollection AddFileStorage(this IServiceCollection services, IConfiguration configuration)
    {
        var extensions = ReadAllowedExtensions(configuration);
        services.Configure<FileSettings>(o => o.AllowedExtensions = extensions);

        // Matched by name, case-insensitively, as DatabaseProviders.Parse matches the database choice.
        var provider = configuration[ProviderKey];
        if (string.IsNullOrWhiteSpace(provider) || IsNamed(provider, "Local"))
        {
            return services.AddLocalDirectoryBlobService(configuration);
        }

        if (IsNamed(provider, "AzureStorage"))
        {
            return services.AddAzureStorageAdapter(configuration);
        }

        if (IsNamed(provider, "AwsS3"))
        {
            return services.AddS3BlobService(configuration);
        }

        throw new InvalidOperationException(
            $"'{provider}' is not a supported value for {ProviderKey}. Use one of: Local, AzureStorage, AwsS3.");
    }

    private static bool IsNamed(string value, string name) =>
        string.Equals(value, name, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    ///     The operator's allow-list in lowercase, or the design's default when the key is absent. Read here rather
    ///     than bound onto a defaulted list: the configuration binder appends to a list's existing items.
    /// </summary>
    private static string[] ReadAllowedExtensions(IConfiguration configuration)
    {
        var section = configuration.GetSection($"{FileSettings.Name}:{nameof(FileSettings.AllowedExtensions)}");
        if (!section.Exists())
        {
            return [.. FileSettings.DefaultAllowedExtensions];
        }

        var extensions = (section.Get<string[]>() ?? [])
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e.Trim().ToLowerInvariant())
            .ToArray();
        return extensions.Length > 0
            ? extensions
            : throw new InvalidOperationException(
                $"{section.Path} is empty: the service stores no file type. List at least one extension or remove the key.");
    }

    #endregion
}
