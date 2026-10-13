using DKNet.AspCore.Idempotency;
using DKNet.StaticData.Api.ApiEndpoints.Files;
using DKNet.StaticData.Api.Configs.Auth;
using DKNet.StaticData.AppServices.Features.FileGroups;
using Microsoft.Net.Http.Headers;

namespace DKNet.StaticData.Api.ApiEndpoints.FileGroups;

/// <summary>
/// The 5 file group routes under <c>/v1/file-groups</c> (spec DRK-2235 §3a). Checks answer in order: token and caller
/// id (401) and app role (403) in the authorization middleware, then the owner (400), then the body (400, spec DRK-2245
/// §6 R4), then on create the idempotency key; an update then needs its version tag (rule R3). Every route keeps the
/// default request limits.
/// </summary>
internal sealed class FileGroupV1Endpoint : IEndpointConfig
{
    #region Properties

    public string GroupEndpoint => "/file-groups";

    public int Version => 1;

    #endregion

    #region Methods

    public void Map(RouteGroupBuilder group)
    {
        // The same group filters as the file routes, in the same order (FileV1Endpoint).
        group.AddEndpointFilter<CallerLogScopeFilter>();
        group.AddEndpointFilter<DependencyFailureFilter>();

        group.MapPost("/", CreateAsync)
            .RequireAuthorization(AuthConfig.WriteRole)
            .RequiredIdempotentKey();

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
        FileGroupFields fields,
        HttpContext context,
        CreateFileGroupHandler handler,
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
            $"/v1/file-groups/{created.GroupId}?{OwnerQuery.Name}={Uri.EscapeDataString(created.Owner)}", created);
    }

    private static async Task<IResult> ListAsync(
        [AsParameters] FileGroupListQuery query,
        ListFileGroupsHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(query, cancellationToken);
        return result.IsFailed ? ErrorAnswers.Problem(result) : TypedResults.Ok(result.Value);
    }

    private static async Task<IResult> ReadAsync(
        Guid groupId,
        HttpContext context,
        ReadFileGroupHandler handler,
        CancellationToken cancellationToken)
    {
        var found = await handler.HandleAsync(groupId, cancellationToken);
        if (found is null)
        {
            return TypedResults.Problem(FileGroupAnswers.NotFound, statusCode: StatusCodes.Status404NotFound);
        }

        SetVersion(context, found);
        return TypedResults.Ok(found);
    }

    private static async Task<IResult> UpdateAsync(
        Guid groupId,
        FileGroupFields fields,
        HttpContext context,
        UpdateFileGroupHandler handler,
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
        DeleteFileGroupHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(groupId, cancellationToken);
        return result.IsFailed ? ErrorAnswers.Problem(result) : TypedResults.NoContent();
    }

    private static void SetVersion(HttpContext context, FileGroupDto group) =>
        context.Response.Headers.ETag = new EntityTagHeaderValue($"\"{group.Version}\"").ToString();

    #endregion
}
