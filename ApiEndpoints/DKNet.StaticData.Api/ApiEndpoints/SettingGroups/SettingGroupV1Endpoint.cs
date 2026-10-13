using DKNet.StaticData.Api.ApiEndpoints.Files;
using DKNet.StaticData.Api.Configs.Auth;
using DKNet.StaticData.AppServices.Features.SettingGroups;
using Microsoft.Net.Http.Headers;

namespace DKNet.StaticData.Api.ApiEndpoints.SettingGroups;

/// <summary>
/// The 5 setting group routes under <c>/v1/ui-setting-groups</c> (spec DRK-2245 §3a). Checks answer in order: token and
/// caller id (401) and app role (403) in the authorization middleware, then the owner (400), then the body; an update
/// then needs its version tag (rule R3). A create takes no idempotency key: the unique name stops a duplicate. Every
/// route keeps the default request limits.
/// </summary>
internal sealed class SettingGroupV1Endpoint : IEndpointConfig
{
    #region Properties

    public string GroupEndpoint => "/ui-setting-groups";

    public int Version => 1;

    #endregion

    #region Methods

    public void Map(RouteGroupBuilder group)
    {
        // The same group filters as the file routes, in the same order (FileV1Endpoint).
        group.AddEndpointFilter<CallerLogScopeFilter>();
        group.AddEndpointFilter<DependencyFailureFilter>();
        group.AddEndpointFilter<OwnerFilter>();

        group.MapPost("/", CreateAsync)
            .RequireAuthorization(AuthConfig.WriteRole);

        group.MapGet("/", ListAsync)
            .RequireAuthorization(AuthConfig.ReadRole);

        group.MapGet("/{groupId:guid}", ReadAsync)
            .RequireAuthorization(AuthConfig.ReadRole);

        group.MapPut("/{groupId:guid}", UpdateAsync)
            .RequireAuthorization(AuthConfig.WriteRole);

        group.MapDelete("/{groupId:guid}", DeleteAsync)
            .RequireAuthorization(AuthConfig.WriteRole);
    }

    private static async Task<IResult> CreateAsync(
        SettingGroupFields fields,
        HttpContext context,
        CreateSettingGroupHandler handler,
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
            $"/v1/ui-setting-groups/{created.GroupId}?{OwnerQuery.Name}={Uri.EscapeDataString(created.Owner)}", created);
    }

    private static async Task<IResult> ListAsync(
        [AsParameters] SettingGroupListQuery query,
        ListSettingGroupsHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(query, cancellationToken);
        return result.IsFailed ? ErrorAnswers.Problem(result) : TypedResults.Ok(result.Value);
    }

    private static async Task<IResult> ReadAsync(
        Guid groupId,
        HttpContext context,
        ReadSettingGroupHandler handler,
        CancellationToken cancellationToken)
    {
        var found = await handler.HandleAsync(groupId, cancellationToken);
        if (found is null)
        {
            return TypedResults.Problem(SettingGroupAnswers.NotFound, statusCode: StatusCodes.Status404NotFound);
        }

        SetVersion(context, found);
        return TypedResults.Ok(found);
    }

    private static async Task<IResult> UpdateAsync(
        Guid groupId,
        SettingGroupFields fields,
        HttpContext context,
        UpdateSettingGroupHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(groupId, fields, IfMatch.Read(context.Request), cancellationToken);
        if (result.IsFailed)
        {
            return ErrorAnswers.Problem(result);
        }

        SetVersion(context, result.Value);
        return TypedResults.Ok(result.Value);
    }

    private static async Task<IResult> DeleteAsync(
        Guid groupId,
        DeleteSettingGroupHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(groupId, cancellationToken);
        return result.IsFailed ? ErrorAnswers.Problem(result) : TypedResults.NoContent();
    }

    private static void SetVersion(HttpContext context, SettingGroupDto group) =>
        context.Response.Headers.ETag = new EntityTagHeaderValue($"\"{group.Version}\"").ToString();

    #endregion
}
