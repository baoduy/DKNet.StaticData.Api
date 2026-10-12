using DKNet.StaticData.Api.ApiEndpoints;
using DKNet.StaticData.AppServices.Features.Files;
using FluentResults;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DKNet.StaticData.App.Tests.Unit.FileGroups;

/// <summary>Brief DRK-2239 §3 rows 7 and 8: every error kind answers as problem details with its own status and message.</summary>
public class ErrorAnswersTests
{
    public static TheoryData<FileErrorKind, int> Kinds => new()
    {
        { FileErrorKind.NotFound, 404 },
        { FileErrorKind.Refused, 400 },
        { FileErrorKind.TooLarge, 413 },
        { FileErrorKind.BytesMissing, 500 },
        { FileErrorKind.StorageUnavailable, 503 },
        { FileErrorKind.Conflict, 409 },
        { FileErrorKind.VersionRequired, 428 },
        { FileErrorKind.VersionStale, 412 }
    };

    [Theory]
    [MemberData(nameof(Kinds))]
    public void EachErrorKindAnswersItsStatus(FileErrorKind kind, int status)
    {
        var answer = ErrorAnswers.Problem(Result.Fail(new FileError(kind, "The reason.")))
            .ShouldBeOfType<ProblemHttpResult>();

        answer.StatusCode.ShouldBe(status);
        answer.ProblemDetails.Detail.ShouldBe("The reason.");
    }
}
