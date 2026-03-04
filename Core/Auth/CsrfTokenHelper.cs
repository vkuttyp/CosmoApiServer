using System.Security.Cryptography;

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

        return token == expected;
    }
}
