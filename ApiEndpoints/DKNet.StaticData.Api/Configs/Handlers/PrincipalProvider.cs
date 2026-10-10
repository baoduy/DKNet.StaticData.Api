using DKNet.StaticData.Api.ApiEndpoints.Files;

namespace DKNet.StaticData.Api.Configs.Handlers;

/// <summary>
/// The owner and the caller of the current call, for DKNet's data-owner hook and filter and its audit stamp. The owner
/// (<see cref="GetOwnershipKey"/>, and so the only accessible key) is the call's validated <c>owner</c> query value;
/// the audit user (<see cref="GetCurrentUser"/>) is the caller id. They are never the same value (ADR-0005, ADR-0016).
/// </summary>
internal sealed class PrincipalProvider(IHttpContextAccessor accessor) : IPrincipalProvider
{
    #region Fields

    private string _email = string.Empty;
    private bool _initialized;
    private string? _currentUser;
    private string? _ownershipKey;
    private string? _subject;
    private string _userName = string.Empty;

    #endregion

    #region Properties

    public Guid ProfileId
    {
        get
        {
            Initialize();
            return Guid.TryParse(_subject, out var id) ? id : Guid.Empty;
        }
    }

    public string Email
    {
        get
        {
            Initialize();
            return _email;
        }
    }

    public string UserName
    {
        get
        {
            Initialize();
            return _userName;
        }
    }

    #endregion

    #region Methods

    public string? GetOwnershipKey()
    {
        Initialize();
        return _ownershipKey;
    }

    public string? GetCurrentUser()
    {
        Initialize();
        return _currentUser;
    }

    private void Initialize()
    {
        var context = accessor.HttpContext;
        if (context == null)
        {
            return;
        }

        if (_initialized)
        {
            return;
        }

        if (context.User.Identity?.IsAuthenticated != true)
        {
            _ownershipKey = SharedConsts.SystemAccount;
            _currentUser = SharedConsts.SystemAccount;
            _initialized = true;
            return;
        }

        _userName = context.User.Identity.Name ?? string.Empty;
        _ownershipKey = OwnerQuery.TryRead(context.Request, out var owner) ? owner : null;
        _currentUser = CallerAccessor.Read(context.User);

        //Get the subject from subject claims, first non-empty wins
        string[] subjectClaimTypes =
        [
            "http://schemas.microsoft.com/identity/claims/objectidentifier", "oid", ClaimTypes.NameIdentifier, "sub"
        ];
        foreach (var claimType in subjectClaimTypes)
        {
            var claim = context.User.FindFirst(c => string.Equals(c.Type, claimType, StringComparison.OrdinalIgnoreCase));
            if (claim != null && !string.IsNullOrWhiteSpace(claim.Value))
            {
                _subject = claim.Value;
                break;
            }
        }

        //Get email
        var email = context.User.FindFirst(c =>
            c.Type.Equals("emails", StringComparison.OrdinalIgnoreCase) ||
            c.Type.Equals("email", StringComparison.OrdinalIgnoreCase));
        if (email != null)
        {
            _email = email.Value;
            if (string.IsNullOrEmpty(_userName))
            {
                _userName = _email;
            }
        }

        _initialized = true;
    }

    #endregion
}
