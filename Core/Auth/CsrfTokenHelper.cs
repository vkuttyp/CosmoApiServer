using System.Security.Cryptography;
using System.Text;

namespace CosmoApiServer.Core.Auth;

public static class CsrfTokenHelper
{
    public static string GenerateToken()
    {
        var bytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes);
    }

    public static bool Validate(string token, string expected)
    {
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(expected))
            return false;

        // Use constant-time comparison to prevent timing attacks
        var tokenBytes = Encoding.UTF8.GetBytes(token);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        return CryptographicOperations.FixedTimeEquals(tokenBytes, expectedBytes);
    }
}
