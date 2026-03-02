namespace CosmoApiServer.Core.Auth;

public sealed class JwtOptions
{
    /// <summary>Secret key used to sign tokens (minimum 32 characters recommended).</summary>
    public string Secret { get; set; } = string.Empty;

    public string Issuer { get; set; } = "CosmoApiServer";
    public string Audience { get; set; } = "CosmoApiServer";

    /// <summary>Token lifetime in minutes.</summary>
    public int ExpiryMinutes { get; set; } = 60;
}
