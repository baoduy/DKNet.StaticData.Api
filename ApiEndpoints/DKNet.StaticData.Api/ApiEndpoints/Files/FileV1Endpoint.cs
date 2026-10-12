using DKNet.AspCore.Idempotency;
using DKNet.StaticData.Api.Configs.Auth;
using DKNet.StaticData.AppServices.Features.Files;
using Microsoft.Net.Http.Headers;

namespace DKNet.StaticData.Api.ApiEndpoints.Files;

/// <summary>
/// The 6 file routes under <c>/v1/files</c> (spec DRK-2206 §3a, DRK-2235 §3a, design <c>03-integration.md</c>). Checks
/// answer in order: token and caller id (401) and app role (403) in the authorization middleware, then the owner (400),
/// then on upload the idempotency key, the form, the file name, its extension, the size and the group; on a link the
/// body, then the version tag, the file, the group and the version.
/// </summary>
internal sealed class FileV1Endpoint : IEndpointConfig
{
    #region Fields

    /// <summary>The 50,000,000-byte file plus room for the form.</summary>
    private const long UploadBodyLimitBytes = 51_000_000;

    private const string FilePart = "file";

    private const string GroupIdPart = "groupId";

    /// <summary>The same answer for a file that never existed and one of another owner (ADR-0005).</summary>
    private const string NotFoundDetail = "The file does not exist.";

    /// <summary>50,000,000 bytes at a slow 2 Mbit/s take about 200 seconds.</summary>
    private static readonly TimeSpan TransferTimeout = TimeSpan.FromSeconds(300);

    #endregion

    #region Properties

    public string GroupEndpoint => "/files";

    public int Version => 1;

    #endregion

    #region Methods

    public void Map(RouteGroupBuilder group)
    {
        // Group filters wrap each route's own filters: the caller's log scope wraps everything, and the owner is
        // checked before the idempotency key.
        group.AddEndpointFilter<CallerLogScopeFilter>();
        group.AddEndpointFilter<DependencyFailureFilter>();
        group.AddEndpointFilter<OwnerFilter>();

        group.MapPost("/", UploadAsync)
            .RequireAuthorization(AuthConfig.WriteRole)
            .RequiredIdempotentKey()
            // Callers send a bearer token, never a cookie.
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(UploadBodyLimitBytes))
            .WithRequestTimeout(TransferTimeout);

        group.MapGet("/", ListAsync)
            .RequireAuthorization(AuthConfig.ReadRole);

        group.MapGet("/{fileId:guid}", ReadAsync)
            .RequireAuthorization(AuthConfig.ReadRole);

        group.MapGet("/{fileId:guid}/content", DownloadAsync)
            .RequireAuthorization(AuthConfig.ReadRole)
            .WithRequestTimeout(TransferTimeout);

        group.MapDelete("/{fileId:guid}", DeleteAsync)
            .RequireAuthorization(AuthConfig.WriteRole);

        group.MapPut("/{fileId:guid}/group", LinkAsync)
            .RequireAuthorization(AuthConfig.WriteRole);
    }

    /// <summary>
    /// Reads the form itself, after the filters: binding an <c>IFormFile</c> parameter would answer 415 or 400 before
    /// the owner and idempotency key are checked.
    /// </summary>
    private static async Task<IResult> UploadAsync(
        HttpContext context,
        UploadFileHandler handler,
        ICallerAccessor caller,
        ILogger<FileV1Endpoint> logger,
        CancellationToken cancellationToken)
    {
        if (!context.Request.HasFormContentType)
        {
            FileLog.UploadRefused(logger, "form", caller.CallerId);
            return TypedResults.Problem("The upload must be multipart/form-data.",
                statusCode: StatusCodes.Status415UnsupportedMediaType);
        }

        IFormCollection? form;
        try
        {
            form = await ReadFormAsync(context.Request, cancellationToken);
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            // The body is over the route's limit, so the file is over the size limit.
            FileLog.UploadRefused(logger, "size", caller.CallerId);
            return TypedResults.Problem($"The file is over {FileSettings.MaxFileSizeBytes} bytes.",
                statusCode: StatusCodes.Status413PayloadTooLarge);
        }

        if (form is null || form.Files.Count != 1 || form.Files[0].Name != FilePart || !TryReadGroupId(form, out var groupId))
        {
            FileLog.UploadRefused(logger, "form", caller.CallerId);
            return TypedResults.Problem(
                "The form must hold exactly 1 part named \"file\" and at most 1 part named \"groupId\" holding a group id.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var upload = form.Files[0];
        await using var content = upload.OpenReadStream();
        var result = await handler.HandleAsync(upload.FileName, content, groupId, cancellationToken);
        if (result.IsFailed)
        {
            return ErrorAnswers.Problem(result);
        }

        var file = result.Value;
        SetVersion(context, file);
        return TypedResults.Created(
            $"/v1/files/{file.FileId}?{OwnerQuery.Name}={Uri.EscapeDataString(file.Owner)}", file);
    }

    /// <summary>
    /// The optional group of the upload: no part besides the file, or exactly 1 <c>groupId</c> part that is a GUID. Any
    /// other part, a second <c>groupId</c> or one that is not a GUID refuses the form.
    /// </summary>
    private static bool TryReadGroupId(IFormCollection form, out Guid? groupId)
    {
        groupId = null;
        if (form.Count == 0)
        {
            return true;
        }

        if (form.Count > 1 || !form.TryGetValue(GroupIdPart, out var values) || values.Count != 1
            || !Guid.TryParse(values[0], out var id))
        {
            return false;
        }

        groupId = id;
        return true;
    }

    /// <summary>The form, or <see langword="null" /> when the body is not a well-formed multipart form.</summary>
    private static async Task<IFormCollection?> ReadFormAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await request.ReadFormAsync(cancellationToken);
        }
        catch (InvalidDataException)
        {
            // Not a multipart body, such as one with no section at all.
            return null;
        }
    }

    private static async Task<IResult> ListAsync(
        [AsParameters] FileListQuery query,
        ListFilesHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(query, cancellationToken);
        return result.IsFailed ? ErrorAnswers.Problem(result) : TypedResults.Ok(result.Value);
    }

    private static async Task<IResult> ReadAsync(
        Guid fileId,
        HttpContext context,
        ReadFileHandler handler,
        CancellationToken cancellationToken)
    {
        var file = await handler.HandleAsync(fileId, cancellationToken);
        if (file is null)
        {
            return TypedResults.Problem(NotFoundDetail, statusCode: StatusCodes.Status404NotFound);
        }

        SetVersion(context, file);
        return TypedResults.Ok(file);
    }

    private static async Task<IResult> DownloadAsync(
        Guid fileId,
        HttpContext context,
        DownloadFileHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(fileId, cancellationToken);
        if (result.IsFailed)
        {
            return ErrorAnswers.Problem(result);
        }

        var file = result.Value;
        context.Response.Headers.XContentTypeOptions = "nosniff";
        context.Response.ContentLength = file.SizeBytes;
        // fileDownloadName writes "attachment" with an ASCII filename and the exact UTF-8 filename*.
        return TypedResults.Stream(file.Content, file.ContentType, file.FileName);
    }

    private static async Task<IResult> DeleteAsync(
        Guid fileId,
        DeleteFileHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(fileId, cancellationToken);
        return result.IsFailed ? ErrorAnswers.Problem(result) : TypedResults.NoContent();
    }

    /// <summary>Checks the body before the version tag (rule R1); the handler checks the rest.</summary>
    private static async Task<IResult> LinkAsync(
        Guid fileId,
        HttpContext context,
        LinkFileHandler handler,
        CancellationToken cancellationToken)
    {
        var (isValid, groupId) = await LinkBody.ReadAsync(context.Request.Body, cancellationToken);
        if (!isValid)
        {
            return TypedResults.Problem(
                "The body must be a JSON object whose \"groupId\" is a group id, or null to unlink.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var result = await handler.HandleAsync(fileId, groupId, IfMatch.Read(context.Request), cancellationToken);
        if (result.IsFailed)
        {
            return ErrorAnswers.Problem(result);
        }

        SetVersion(context, result.Value);
        return TypedResults.Ok(result.Value);
    }

    private static void SetVersion(HttpContext context, StoredFileDto file) =>
        context.Response.Headers.ETag = new EntityTagHeaderValue($"\"{file.Version}\"").ToString();

    #endregion
}
