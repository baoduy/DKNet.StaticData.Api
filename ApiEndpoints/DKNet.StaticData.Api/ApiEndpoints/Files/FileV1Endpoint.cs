using DKNet.AspCore.Extensions.Endpoints;
using DKNet.AspCore.Idempotency;
using DKNet.StaticData.Api.Configs.Auth;
using DKNet.StaticData.AppServices.Features.Files;
using DKNet.StaticData.Domains.Features.Files.Entities;
using Microsoft.Net.Http.Headers;

namespace DKNet.StaticData.Api.ApiEndpoints.Files;

/// <summary>
/// The 5 file routes under <c>/v1/files</c> (spec DRK-2206 §3a, design <c>03-integration.md</c>). Checks answer in
/// order: token and caller id (401) and app role (403) in the authorization middleware, then the owner (400), then on
/// upload the idempotency key, the form, the file name, its extension and the size.
/// </summary>
internal sealed class FileV1Endpoint : IEndpointConfig
{
    #region Fields

    /// <summary>The 50,000,000-byte file plus room for the form.</summary>
    private const long UploadBodyLimitBytes = 51_000_000;

    private const string FilePart = "file";

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
        // Group filters wrap each route's own filters: the owner is checked before the idempotency key.
        group.AddEndpointFilter<DependencyFailureFilter>();
        group.AddEndpointFilter<OwnerFilter>();

        group.MapPost("/", UploadAsync)
            .RequireAuthorization(AuthConfig.WriteRole)
            .RequiredIdempotentKey()
            // Callers send a bearer token, never a cookie.
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(UploadBodyLimitBytes))
            .WithRequestTimeout(TransferTimeout);

        group.MapGetList<StoredFile, StoredFileDto>()
            .RequireAuthorization(AuthConfig.ReadRole)
            // DKNet's list route turns any exception into a 500 in a filter of its own, which the group's filter
            // never sees; this one runs inside it.
            .AddEndpointFilter<DependencyFailureFilter>();

        group.MapGet("/{fileId:guid}", ReadAsync)
            .RequireAuthorization(AuthConfig.ReadRole);

        group.MapGet("/{fileId:guid}/content", DownloadAsync)
            .RequireAuthorization(AuthConfig.ReadRole)
            .WithRequestTimeout(TransferTimeout);

        group.MapDelete("/{fileId:guid}", DeleteAsync)
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

        if (form is null || form.Files.Count != 1 || form.Files[0].Name != FilePart || form.Count > 0)
        {
            FileLog.UploadRefused(logger, "form", caller.CallerId);
            return TypedResults.Problem("The form must hold exactly 1 part, named \"file\".",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var upload = form.Files[0];
        await using var content = upload.OpenReadStream();
        var result = await handler.HandleAsync(upload.FileName, content, cancellationToken);
        if (result.IsFailed)
        {
            return Problem(result);
        }

        var file = result.Value;
        SetVersion(context, file);
        return TypedResults.Created(
            $"/v1/files/{file.FileId}?{OwnerQuery.Name}={Uri.EscapeDataString(file.Owner)}", file);
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
            return null;
        }
        catch (IOException ex) when (ex is not BadHttpRequestException)
        {
            // A multipart body with no section ends before its first boundary.
            return null;
        }
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
            return TypedResults.NotFound();
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
            return Problem(result);
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
        return result.IsFailed ? Problem(result) : TypedResults.NoContent();
    }

    private static void SetVersion(HttpContext context, StoredFileDto file) =>
        context.Response.Headers.ETag = new EntityTagHeaderValue($"\"{file.Version}\"").ToString();

    private static IResult Problem(IResultBase result)
    {
        var error = result.Errors.OfType<FileError>().First();
        return error.Kind == FileErrorKind.NotFound
            ? TypedResults.NotFound()
            : TypedResults.Problem(error.Message, statusCode: error.Kind switch
            {
                FileErrorKind.Refused => StatusCodes.Status400BadRequest,
                FileErrorKind.TooLarge => StatusCodes.Status413PayloadTooLarge,
                FileErrorKind.BytesMissing => StatusCodes.Status500InternalServerError,
                _ => StatusCodes.Status503ServiceUnavailable
            });
    }

    #endregion
}
