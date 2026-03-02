using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace CosmoApiServer.Core.Auth;

public sealed class JwtService
{
    private readonly JwtOptions _options;
    private readonly SymmetricSecurityKey _key;
    private readonly JwtSecurityTokenHandler _handler = new();

    public JwtService(JwtOptions options)
    {
        _options = options;
        _key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Secret));
    }

    /// <summary>Generates a signed JWT containing the provided claims.</summary>
    public string GenerateToken(IEnumerable<Claim> claims)
    {
        var credentials = new SigningCredentials(_key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddMinutes(_options.ExpiryMinutes),
            signingCredentials: credentials);

        return _handler.WriteToken(token);
    }

    /// <summary>
    /// Validates a JWT string. Returns the ClaimsPrincipal on success, null on failure.
    /// </summary>
    public ClaimsPrincipal? ValidateToken(string token)
    {
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
                ClockSkew = TimeSpan.Zero
            };

            return _handler.ValidateToken(token, parameters, out _);
        }
        catch
        {
            return null;
        }
    }
}
