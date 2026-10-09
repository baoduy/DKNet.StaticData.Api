using DKNet.StaticData.Api.Configs;
using DKNet.StaticData.Api.Configs.AzureAppConfig;
using DKNet.StaticData.Api.Configs.Jobs;
using SharpGrip.FluentValidation.AutoValidation.Endpoints.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Rebind features after potentially loading from Azure App Configuration
var feature = builder.Configuration.GetSection(FeatureOptions.Name).Get<FeatureOptions>() ?? new FeatureOptions();

// Configuration sources first, so a job resolves its settings (the connection string included) from exactly the
// same sources the in-process start-up migration does — Azure App Configuration included.
builder.AddLogConfig(feature)
    .AddAzureAppConfig(feature);

// The database choice is read once its sources are loaded and before the job dispatch and any app service is
// registered, so an unknown value stops the service before the host is built (DRK-2198 R1).
var database = DatabaseConfig.ResolveProvider(builder.Configuration);

// The service decides what to do from its process arguments alone (R1) — no environment check, no configuration
// value. Dispatched before any service registration and before anything that could serve traffic or connect to
// the message bus (§3 row 2), so a job run loads only what the job needs (R4).
var jobSelection = JobSelector.Select(args, JobRegistry.Jobs.Keys.ToArray());
if (jobSelection.HasJobName)
{
    if (!jobSelection.IsRecognized)
    {
        await Console.Error.WriteLineAsync(
            $"Unrecognized job \"{jobSelection.RequestedJobName}\". Known jobs: {string.Join(", ", jobSelection.KnownJobNames)}");
        return 1;
    }

    return await JobRegistry.Jobs[jobSelection.RequestedJobName!](builder);
}

builder.AddFluentValidationConfig();

//Run migration (when configured to) and continue to serve.
await builder.RunMigrationAsync(feature, database);

// Add services to the container.
builder.Services
    .AddOptions(builder.Configuration)
    .AddAppConfig(feature, builder.Configuration, database)
    // Populates [FromClaim] members (e.g. ByUser) before validation and before the handler, from the
    // authenticated caller's own claims.
    .AddContextualRequestPopulation();

await builder.Build()
    .UseAppConfig(a => a.UseEndpointConfigs(o =>
    {
        o.RequireAuthorization = feature.RequireAuthorization;
        o.EnableVersioning = feature.EnableVersioning;
        o.ConfigureGroup = (group, _) => group.AddFluentValidationAutoValidation();
    }, typeof(Program).Assembly));

return 0;

//This Startup endpoint for Unit Tests
namespace DKNet.StaticData.Api
{
    public class Program;
}