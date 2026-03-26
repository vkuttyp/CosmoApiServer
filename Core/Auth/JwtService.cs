using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace CosmoApiServer.Core.Auth;

public sealed class JwtService
{
    private readonly JwtOptions _options;
    private readonly SymmetricSecurityKey _key;
    private readonly JsonWebTokenHandler _handler = new();

    public JwtService(JwtOptions options)
    {
        _options = options;
        _key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Secret));
    }

    /// <summary>Generates a signed JWT containing the provided claims.</summary>
    public string GenerateToken(IEnumerable<Claim> claims)
    {
        var credentials = new SigningCredentials(_key, SecurityAlgorithms.HmacSha256);
        
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(_options.ExpiryMinutes),
            SigningCredentials = credentials
        };

        return _handler.CreateToken(descriptor);
    }

    /// <summary>
    /// Validates a JWT string. Returns the ClaimsPrincipal on success, null on failure.
    /// </summary>
    public async ValueTask<ClaimsPrincipal?> ValidateTokenAsync(string token)
    {
        if (string.IsNullOrEmpty(token))
            return null;

        try
        {
            var parameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = _key,
                ValidateIssuer = true,
                ValidIssuer = _options.Issuer,
                ValidateAudience = true,
                ValidAudience = _options.Audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(5)
            };

            var result = await _handler.ValidateTokenAsync(token, parameters);
            return result.IsValid ? new ClaimsPrincipal(result.ClaimsIdentity) : null;
        }
        catch
        {
            return null;
        }
    }
}
