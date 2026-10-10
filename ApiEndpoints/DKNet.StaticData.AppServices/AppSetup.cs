using DKNet.AspCore.Extensions.Endpoints;
using DKNet.StaticData.AppServices.Features.Files;

namespace DKNet.StaticData.AppServices;

/// <summary>
///
/// </summary>
public static class AppSetup
{
    #region Methods

    /// <summary>
    ///
    /// </summary>
    /// <param name="services"></param>
    /// <returns></returns>
    public static IServiceCollection AddAppServices(this IServiceCollection services)
    {
        TypeAdapterConfig.GlobalSettings.Default.NameMatchingStrategy(NameMatchingStrategy.Flexible);
        TypeAdapterConfig.GlobalSettings.Default.MapToConstructor(true);
        TypeAdapterConfig.GlobalSettings.Default.PreserveReference(true);
        TypeAdapterConfig.GlobalSettings.ScanMaps();
        TypeAdapterConfig.GlobalSettings.Compile();

        services
            .AddSingleton(TypeAdapterConfig.GlobalSettings)
            .AddScoped<IMapper, ServiceMapper>()
            .AddScoped<UploadFileHandler>()
            .AddScoped<ReadFileHandler>()
            .AddScoped<DownloadFileHandler>()
            .AddScoped<DeleteFileHandler>();

        // DKNet's list routes default to a 3-month activity window when neither fromDate nor toDate is sent. This
        // service's lists return every record of the owner, however old (design 03-integration.md, 04-data.md).
        services.AddListQueryOptions(options => options.DefaultActivityWindowMonths = 0);

        return services;
    }

    #endregion
}