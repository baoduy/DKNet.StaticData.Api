namespace DKNet.StaticData.Api.ApiEndpoints.Files;

/// <summary>
/// The <c>owner</c> query parameter every <c>/v1</c> route needs (ADR-0005): exactly one value of 1 to 255
/// characters, not only white space, with no control character. It is kept exactly as sent — never trimmed or
/// case-folded.
/// </summary>
internal static class OwnerQuery
{
    #region Fields

    public const string Name = "owner";

    private const int MaxLength = 255;

    #endregion

    #region Methods

    /// <summary>Whether <paramref name="request" /> carries a well-formed owner, and that owner.</summary>
    public static bool TryRead(HttpRequest request, out string owner)
    {
        var values = request.Query[Name];
        owner = values.Count == 1 ? values[0] ?? string.Empty : string.Empty;
        return owner.Length <= MaxLength && !string.IsNullOrWhiteSpace(owner) && !owner.Any(char.IsControl);
    }

    #endregion
}

/// <summary>Refuses a call with a missing or malformed owner with 400, as problem details, before any input check.</summary>
internal sealed class OwnerFilter : IEndpointFilter
{
    #region Methods

    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next) =>
        OwnerQuery.TryRead(context.HttpContext.Request, out _)
            ? next(context)
            : ValueTask.FromResult<object?>(TypedResults.Problem(
                "The owner query parameter is required: 1 to 255 characters, not only white space, no control character.",
                statusCode: StatusCodes.Status400BadRequest));

    #endregion
}
