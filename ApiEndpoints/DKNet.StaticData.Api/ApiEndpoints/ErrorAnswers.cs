using DKNet.StaticData.AppServices.Features.Files;

namespace DKNet.StaticData.Api.ApiEndpoints;

/// <summary>Answers a failed file or file group action as problem details with the status its <see cref="FileErrorKind" /> names.</summary>
internal static class ErrorAnswers
{
    #region Methods

    public static IResult Problem(IResultBase result)
    {
        var error = result.Errors.OfType<FileError>().First();
        return TypedResults.Problem(error.Message, statusCode: error.Kind switch
        {
            FileErrorKind.NotFound => StatusCodes.Status404NotFound,
            FileErrorKind.Refused => StatusCodes.Status400BadRequest,
            FileErrorKind.TooLarge => StatusCodes.Status413PayloadTooLarge,
            FileErrorKind.BytesMissing => StatusCodes.Status500InternalServerError,
            FileErrorKind.Conflict => StatusCodes.Status409Conflict,
            FileErrorKind.VersionRequired => StatusCodes.Status428PreconditionRequired,
            FileErrorKind.VersionStale => StatusCodes.Status412PreconditionFailed,
            _ => StatusCodes.Status503ServiceUnavailable
        });
    }

    #endregion
}
