using FluentValidation;
using FluentValidation.Results;
using SharpGrip.FluentValidation.AutoValidation.Endpoints.Results;

namespace DKNet.StaticData.Api.ApiEndpoints.Files;

/// <summary>
/// The <c>owner</c> query parameter every <c>/v1</c> route needs (ADR-0005): exactly one value, a caller text of the text
/// rules (spec DRK-2245 §3) — no control character as sent, then trimmed, then 1 to 255 characters. The owner is the
/// trimmed text, its case kept.
/// </summary>
internal static class OwnerQuery
{
    #region Fields

    public const string Name = "owner";

    private const int MaxLength = 255;

    private static readonly InlineValidator<SentOwner> Rule = new()
    {
        v => v.RuleFor(o => o.Owner).CallerText(MaxLength, required: true).OverridePropertyName(Name)
    };

    #endregion

    #region Methods

    /// <summary>Whether <paramref name="request" /> carries a well-formed owner, and that owner, trimmed.</summary>
    public static bool TryRead(HttpRequest request, out string owner) => Check(request, out owner).IsValid;

    /// <summary>The owner check of <paramref name="request" />, and the owner, trimmed; two or more values are none.</summary>
    public static ValidationResult Check(HttpRequest request, out string owner)
    {
        var values = request.Query[Name];
        var sent = values.Count == 1 ? values.ToString() : string.Empty;
        owner = TextRules.Trim(sent)!;
        return Rule.Validate(new SentOwner(sent));
    }

    #endregion

    internal sealed record SentOwner(string? Owner);
}

/// <summary>
/// Refuses a call with a missing or malformed owner with 400, as problem details in the shape of every other refused
/// input, before any input check.
/// </summary>
internal sealed class OwnerFilter : IEndpointFilter
{
    #region Methods

    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var check = OwnerQuery.Check(context.HttpContext.Request, out _);
        return check.IsValid
            ? next(context)
            : ValueTask.FromResult<object?>(context.HttpContext.RequestServices
                .GetRequiredService<IFluentValidationAutoValidationResultFactory>().CreateResult(context, check));
    }

    #endregion
}
