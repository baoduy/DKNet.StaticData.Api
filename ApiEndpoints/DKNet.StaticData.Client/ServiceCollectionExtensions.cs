using Microsoft.Extensions.DependencyInjection;

namespace DKNet.StaticData.Client;

/// <summary>
/// Registers <see cref="IStaticDataFileClient"/> as a typed <see cref="HttpClient"/> pointed at the service. Adds no
/// credential, handler or header of its own.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Registers <see cref="IStaticDataFileClient"/> with no message handler of its own.</summary>
    public static IServiceCollection AddStaticDataClient(this IServiceCollection services, Uri baseAddress)
    {
        services.AddHttpClient<IStaticDataFileClient, StaticDataFileClient>(c => c.BaseAddress = baseAddress);
        return services;
    }

    /// <summary>
    /// Registers <see cref="IStaticDataFileClient"/> and chains <paramref name="messageHandlerType"/> — a
    /// <see cref="DelegatingHandler"/> the application registered — onto every request, so the application attaches
    /// its own credential. Leave the handler's <see cref="DelegatingHandler.InnerHandler"/> unset: the client
    /// factory chains it.
    /// </summary>
    public static IServiceCollection AddStaticDataClient(
        this IServiceCollection services,
        Uri baseAddress,
        Type messageHandlerType)
    {
        services.AddHttpClient<IStaticDataFileClient, StaticDataFileClient>(c => c.BaseAddress = baseAddress)
            .AddHttpMessageHandler(sp => (DelegatingHandler)sp.GetRequiredService(messageHandlerType));
        return services;
    }
}
