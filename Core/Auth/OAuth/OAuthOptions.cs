namespace CosmoApiServer.Core.Auth.OAuth;

public sealed class OAuthOptions
{
    /// <summary>OIDC authority (e.g., https://accounts.google.com, https://login.microsoftonline.com/{tenant}).</summary>
    public string? Authority { get; set; }

    /// <summary>Expected audience claim in the access token.</summary>
    public string? Audience { get; set; }

    /// <summary>Expected issuer claim. If null, derived from Authority.</summary>
    public string? Issuer { get; set; }

    /// <summary>If true, fetch OIDC discovery document from {Authority}/.well-known/openid-configuration.</summary>
    public bool UseDiscovery { get; set; } = true;

    /// <summary>How long to cache the JWKS (signing keys). Default: 1 hour.</summary>
    public TimeSpan JwksCacheLifetime { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Validate token audience. Default: true.</summary>
    public bool ValidateAudience { get; set; } = true;

    /// <summary>Validate token issuer. Default: true.</summary>
    public bool ValidateIssuer { get; set; } = true;

    /// <summary>Validate token lifetime. Default: true.</summary>
    public bool ValidateLifetime { get; set; } = true;

    /// <summary>Clock skew tolerance. Default: 5 minutes.</summary>
    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Optional: map additional claims to ClaimsPrincipal.
    /// Called after successful token validation.
    /// </summary>
    public Action<System.Security.Claims.ClaimsPrincipal>? OnTokenValidated { get; set; }
}
