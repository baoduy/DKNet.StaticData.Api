using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace DKNet.StaticData.App.TestSupport;

/// <summary>
/// Signed bearer tokens for a test host that keeps the service's own JWT bearer scheme. The token goes through the real
/// <c>JwtBearerHandler</c> and whatever claim mapping the service's bearer setup applies, so a claim the service reads
/// under the wrong name (<c>roles</c>, <c>client_id</c>, <c>azp</c>, <c>appid</c>) shows in a test. Only the trust is
/// swapped: <see cref="TrustTestIssuer"/> points the bearer options at this class's signing key, issuer and audience
/// instead of the issuer metadata.
/// </summary>
public static class TestTokens
{
    public const string Issuer = "https://issuer.staticdata.test/";
    public const string Audience = "api://staticdata-tests";

    private static readonly SymmetricSecurityKey SigningKey = new(RandomNumberGenerator.GetBytes(32));

    /// <summary>
    /// A token carrying exactly <paramref name="claims"/>. A claim value that is a <see cref="string"/> array is
    /// written as a JSON array. The token is valid for 5 minutes from now unless <paramref name="expires"/> is given.
    /// </summary>
    public static string Mint(IReadOnlyDictionary<string, object> claims, DateTime? expires = null)
    {
        var notAfter = expires ?? DateTime.UtcNow.AddMinutes(5);
        var notBefore = notAfter.AddMinutes(-10);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            Claims = claims.ToDictionary(c => c.Key, c => c.Value, StringComparer.Ordinal),
            IssuedAt = notBefore,
            NotBefore = notBefore,
            Expires = notAfter,
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256)
        });
    }

    /// <summary>
    /// Makes the service's own bearer scheme trust <see cref="Mint"/>'s tokens. Mutates the bound options rather than
    /// replacing <see cref="TokenValidationParameters"/>, so the service's own settings on it (claim mapping and name
    /// claim among them) stay as the service sets them. Call from a test factory's <c>ConfigureTestServices</c>.
    /// </summary>
    public static void TrustTestIssuer(IServiceCollection services) =>
        services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.MetadataAddress = null!;
            options.Authority = null;
            options.ConfigurationManager = null;
            options.Configuration = null;
            options.TokenValidationParameters.ValidIssuer = Issuer;
            options.TokenValidationParameters.ValidAudience = Audience;
            options.TokenValidationParameters.IssuerSigningKey = SigningKey;
        });
}
