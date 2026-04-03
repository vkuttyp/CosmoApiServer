using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using CosmoApiServer.Core.Http;
using CosmoApiServer.Core.Middleware;
using Microsoft.IdentityModel.Tokens;

namespace CosmoApiServer.Core.Auth.OAuth;

/// <summary>
/// Validates OAuth2 Bearer tokens against an OIDC provider.
/// Fetches the JWKS from {Authority}/.well-known/openid-configuration and
/// validates incoming tokens on every request (keys cached per JwksCacheLifetime).
/// Non-blocking — 401s are enforced by [Authorize].
/// </summary>
public sealed class OAuthMiddleware(OAuthOptions options) : IMiddleware
{
    private readonly JwtSecurityTokenHandler _handler = new() { MapInboundClaims = false };
    private JsonWebKeySet? _jwks;
    private DateTimeOffset _jwksExpiry = DateTimeOffset.MinValue;
    private string? _resolvedIssuer;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async ValueTask InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (context.Request.Headers.TryGetValue("Authorization", out var authHeader) &&
            authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var token = authHeader["Bearer ".Length..].Trim().Trim('"');
            try
            {
                var principal = await ValidateTokenAsync(token);
                if (principal is not null)
                {
                    context.User = principal;
                    options.OnTokenValidated?.Invoke(principal);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[OAuth] Token validation failed: {ex.Message}");
            }
        }

        await next(context);
    }

    private async Task<ClaimsPrincipal?> ValidateTokenAsync(string token)
    {
        var keys = await GetSigningKeysAsync();
        if (keys is null) return null;

        var issuer = _resolvedIssuer ?? options.Issuer ?? options.Authority;

        var parameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys        = keys,
            ValidateIssuer           = options.ValidateIssuer,
            ValidIssuer              = issuer,
            ValidateAudience         = options.ValidateAudience,
            ValidAudience            = options.Audience,
            ValidateLifetime         = options.ValidateLifetime,
            ClockSkew                = options.ClockSkew
        };

        var result = await _handler.ValidateTokenAsync(token, parameters);
        return result.IsValid ? new ClaimsPrincipal(result.ClaimsIdentity) : null;
    }

    private async Task<IEnumerable<SecurityKey>?> GetSigningKeysAsync()
    {
        if (_jwks is not null && DateTimeOffset.UtcNow < _jwksExpiry)
            return _jwks.GetSigningKeys();

        await _lock.WaitAsync();
        try
        {
            if (_jwks is not null && DateTimeOffset.UtcNow < _jwksExpiry)
                return _jwks.GetSigningKeys();

            if (options.Authority is null) return null;

            using var http = new HttpClient();

            string jwksUri;
            if (options.UseDiscovery)
            {
                var discoveryUrl = $"{options.Authority.TrimEnd('/')}/.well-known/openid-configuration";
                var discovery = await http.GetStringAsync(discoveryUrl);
                using var doc = JsonDocument.Parse(discovery);
                jwksUri = doc.RootElement.GetProperty("jwks_uri").GetString()!;

                if (options.ValidateIssuer && _resolvedIssuer is null &&
                    doc.RootElement.TryGetProperty("issuer", out var iss))
                    _resolvedIssuer = iss.GetString();
            }
            else
            {
                jwksUri = $"{options.Authority.TrimEnd('/')}/.well-known/jwks.json";
            }

            var jwksJson = await http.GetStringAsync(jwksUri);
            _jwks = new JsonWebKeySet(jwksJson);
            _jwksExpiry = DateTimeOffset.UtcNow.Add(options.JwksCacheLifetime);

            return _jwks.GetSigningKeys();
        }
        finally
        {
            _lock.Release();
        }
    }
}
