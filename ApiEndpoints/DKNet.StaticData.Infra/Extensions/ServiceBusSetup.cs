using System.Diagnostics.CodeAnalysis;
using DKNet.StaticData.Infra.Contexts;

namespace DKNet.StaticData.Infra.Extensions;

[ExcludeFromCodeCoverage]
public static class ServiceBusSetup
{
    #region Methods

    internal static MessageBusBuilder AddMemoryBus(this MessageBusBuilder builder, Assembly serviceAssembly)
    {
        //Memory bus to handle the internal MediatR-Like processes
        builder.AddChildBus(
            "ImMemory",
            me =>

                //https://github.com/zarusz/SlimMessageBus/blob/master/docs/provider_memory.md
                me.WithProviderMemory(cf =>
                    {
                        cf.EnableMessageHeaders = false;
                        cf.EnableMessageSerialization = false;
                        cf.EnableBlockingPublish = false;
                    })
                    .AutoDeclareFrom(serviceAssembly)
                    .AddServicesFromAssembly(serviceAssembly));

        return builder;
    }

    public static IServiceCollection AddServiceBus(this IServiceCollection service, Assembly serviceAssembly)
    {
        service.AddSlimBusEfCoreInterceptor<CoreDbContext>()
            .AddSlimMessageBus(mbb =>
        {
            //This is a global config for all the child buses
            mbb.AddJsonSerializer();

            mbb.AddMemoryBus(serviceAssembly);
        });

        return service;
    }

    #endregion
}