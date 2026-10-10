using DKNet.StaticData.Share.Options;

namespace DKNet.StaticData.Infra.Extensions;

/// <summary>
///     Registers the file settings and the blob storage provider the operator chose.
/// </summary>
public static class FileStorageSetup
{
    #region Methods

    /// <summary>
    ///     Binds <see cref="FileSettings" /> from <c>Files</c> and registers the blob storage provider named by
    ///     <c>BlobStorage:Provider</c>.
    /// </summary>
    /// <param name="services">The service collection used to register dependencies.</param>
    /// <param name="configuration">The service's configuration.</param>
    /// <returns>The same <see cref="IServiceCollection" /> instance for chaining.</returns>
    public static IServiceCollection AddFileStorage(this IServiceCollection services, IConfiguration configuration) =>
        throw new NotImplementedException();

    #endregion
}
