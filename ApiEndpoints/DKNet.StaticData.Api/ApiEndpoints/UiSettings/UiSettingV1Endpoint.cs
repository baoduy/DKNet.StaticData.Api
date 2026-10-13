using DKNet.StaticData.Api.ApiEndpoints.Files;
using DKNet.StaticData.Api.Configs.Auth;
using DKNet.StaticData.AppServices.Features.UiSettings;
using Microsoft.Net.Http.Headers;

namespace DKNet.StaticData.Api.ApiEndpoints.UiSettings;

/// <summary>
/// The 5 UI setting routes and the erase route under <c>/v1/ui-settings</c> (spec DRK-2245 §3a). Checks answer in order:
/// token and caller id (401) and app role (403) in the authorization middleware, then the owner (400, every
/// <c>/v1</c> group's filter), then the body; an update then needs its version tag (rule R2). A create takes no
/// idempotency key: the unique setting key stops a duplicate. The id routes take only a GUID, so <c>/all</c> is always the erase. Every route keeps the default request
/// limits.
/// </summary>
internal sealed class UiSettingV1Endpoint : IEndpointConfig
{
    #region Properties

    public string GroupEndpoint => "/ui-settings";

    public int Version => 1;

    #endregion

    #region Methods

    public void Map(RouteGroupBuilder group)
    {
        // The same group filters as the file routes, in the same order (FileV1Endpoint).
        group.AddEndpointFilter<CallerLogScopeFilter>();
        group.AddEndpointFilter<DependencyFailureFilter>();

        group.MapPost("/", CreateAsync)
            .RequireAuthorization(AuthConfig.WriteRole);

        group.MapGet("/", ListAsync)
            .RequireAuthorization(AuthConfig.ReadRole);

        group.MapGet("/{settingId:guid}", ReadAsync)
            .RequireAuthorization(AuthConfig.ReadRole);

        group.MapPut("/{settingId:guid}", UpdateAsync)
            .RequireAuthorization(AuthConfig.WriteRole);

        group.MapDelete("/{settingId:guid}", DeleteAsync)
            .RequireAuthorization(AuthConfig.WriteRole);

        group.MapDelete("/all", EraseAsync)
            .RequireAuthorization(AuthConfig.WriteRole);
    }

    private static async Task<IResult> CreateAsync(
        NewUiSettingFields fields,
        HttpContext context,
        CreateUiSettingHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(fields, cancellationToken);
        if (result.IsFailed)
        {
            return ErrorAnswers.Problem(result);
        }

        var created = result.Value;
        SetVersion(context, created);
        return TypedResults.Created(
            $"/v1/ui-settings/{created.SettingId}?{OwnerQuery.Name}={Uri.EscapeDataString(created.Owner)}", created);
    }

    private static async Task<IResult> ListAsync(
        [AsParameters] UiSettingListQuery query,
        ListUiSettingsHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(query, cancellationToken);
        return result.IsFailed ? ErrorAnswers.Problem(result) : TypedResults.Ok(result.Value);
    }

    private static async Task<IResult> ReadAsync(
        Guid settingId,
        HttpContext context,
        ReadUiSettingHandler handler,
        CancellationToken cancellationToken)
    {
        var found = await handler.HandleAsync(settingId, cancellationToken);
        if (found is null)
        {
            return TypedResults.Problem(UiSettingAnswers.NotFound, statusCode: StatusCodes.Status404NotFound);
        }

        SetVersion(context, found);
        return TypedResults.Ok(found);
    }

    private static async Task<IResult> UpdateAsync(
        Guid settingId,
        UiSettingFields fields,
        HttpContext context,
        UpdateUiSettingHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(settingId, fields, IfMatch.Read(context.Request), cancellationToken);
        if (result.IsFailed)
        {
            return ErrorAnswers.Problem(result);
        }

        SetVersion(context, result.Value);
        return TypedResults.Ok(result.Value);
    }

    private static async Task<IResult> DeleteAsync(
        Guid settingId,
        DeleteUiSettingHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(settingId, cancellationToken);
        return result.IsFailed ? ErrorAnswers.Problem(result) : TypedResults.NoContent();
    }

    private static async Task<IResult> EraseAsync(EraseUiSettingsHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(cancellationToken);
        return result.IsFailed ? ErrorAnswers.Problem(result) : TypedResults.NoContent();
    }

    private static void SetVersion(HttpContext context, UiSettingDto setting) =>
        context.Response.Headers.ETag = new EntityTagHeaderValue($"\"{setting.Version}\"").ToString();

    #endregion
}
