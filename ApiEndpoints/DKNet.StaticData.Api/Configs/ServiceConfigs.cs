using DKNet.EfCore.Extensions.Serialization;
using DKNet.StaticData.Infra.Contexts;
using JsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace DKNet.StaticData.Api.Configs;

[ExcludeFromCodeCoverage]
internal static class ServiceConfigs
{
    #region Methods

    public static IServiceCollection AddAllAppServices(
        this IServiceCollection services,
        DatabaseProvider database)
    {
        services
            .AddSingleton<IHttpContextAccessor, HttpContextAccessor>()
            .AddSingleton<ISensitiveDataPrincipalAccessor, HttpContextSensitiveDataPrincipalAccessor>()
            .AddScoped<IPrincipalProvider, PrincipalProvider>()
            .AddScoped<ICallerAccessor, CallerAccessor>()
            // Also wires DKNet's DataOwnerHook onto CoreDbContext: it stamps CreatedBy/CreatedOn from
            // IDataOwnerProvider on save, never from a request property — a generated create request can
            // never set the acting user (DRK-715 R1).
            .AddDataOwnerProvider<CoreDbContext, PrincipalProvider>()
            // Stamps CreatedBy/UpdatedBy from the caller id, never from the owner that OwnedBy holds (ADR-0016).
            .AddCurrentUserProvider<CoreDbContext, PrincipalProvider>();

        services
            .AddAppServices()
            .AddInfraServices(DatabaseConfig.UseDatabase(database))

            //Service Bus
            .AddServiceBus(typeof(AppSetup).Assembly);

        return services;
    }

    public static IServiceCollection AddOptions(this IServiceCollection services, IConfiguration configuration)
    {
        // Configure core options for the application
        services.Configure<FeatureOptions>(configuration.GetSection(FeatureOptions.Name));

        services.ConfigureHttpJsonOptions(op =>
        {
            op.SerializerOptions.PropertyNamingPolicy = SharedConsts.JsonSerializerOptions.PropertyNamingPolicy;
            op.SerializerOptions.DefaultIgnoreCondition = SharedConsts.JsonSerializerOptions.DefaultIgnoreCondition;
            op.SerializerOptions.WriteIndented = SharedConsts.JsonSerializerOptions.WriteIndented;
            op.SerializerOptions.PropertyNameCaseInsensitive =
                SharedConsts.JsonSerializerOptions.PropertyNameCaseInsensitive;
            op.SerializerOptions.DictionaryKeyPolicy = SharedConsts.JsonSerializerOptions.DictionaryKeyPolicy;

            op.SerializerOptions.Converters.Clear();
            foreach (var converter in SharedConsts.JsonSerializerOptions.Converters)
            {
                op.SerializerOptions.Converters.Add(converter);
            }
        });

        // ConfigureHttpJsonOptions above has no service-provider access, so the role-aware sensitive-data
        // opt-in (needs ISensitiveDataPrincipalAccessor from DI) goes through this factory registration
        // instead — same JsonOptions instance, applied once at start-up, per the DKNet doc's recipe.
        services.AddSingleton<IConfigureOptions<JsonOptions>>(sp =>
            new ConfigureOptions<JsonOptions>(op =>
                op.SerializerOptions.UseRoleAwareSensitiveData(
                    sp.GetRequiredService<ISensitiveDataPrincipalAccessor>())));

        return services;
    }

    #endregion
}