using System.Net;
using DKNet.StaticData.Client.Contracts;

namespace DKNet.StaticData.Client;

/// <summary>
/// Thrown when the service answers a call with a non-success status. Carries the status code and the problem
/// details the service sent, so the caller never reads the answer's JSON itself.
/// </summary>
public sealed class StaticDataApiException : Exception
{
    public StaticDataApiException(HttpStatusCode statusCode, StaticDataProblemDetails? problemDetails, string message)
        : base(message)
    {
        StatusCode = statusCode;
        ProblemDetails = problemDetails;
    }

    /// <summary>The answer's HTTP status code.</summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>The answer's problem details; <see langword="null"/> when the body held none.</summary>
    public StaticDataProblemDetails? ProblemDetails { get; }
}
