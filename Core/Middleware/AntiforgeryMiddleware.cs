using System.Security.Cryptography;
using CosmoApiServer.Core.Http;

namespace CosmoApiServer.Core.Middleware;

// ── Options ───────────────────────────────────────────────────────────────────

public sealed class AntiforgeryOptions
{
    /// <summary>Cookie name for the antiforgery token.</summary>
    public string CookieName { get; set; } = "__RequestVerificationToken";

    /// <summary>Form field name or header name for the request token.</summary>
    public string HeaderName { get; set; } = "X-XSRF-TOKEN";

    /// <summary>Form field name for the request token.</summary>
    public string FormFieldName { get; set; } = "__RequestVerificationToken";
}

// ── Service ───────────────────────────────────────────────────────────────────

public interface IAntiforgeryService
{
    /// <summary>Gets or creates the antiforgery token pair for this request.</summary>
    AntiforgeryTokenSet GetAndStoreTokens(HttpContext context);

    /// <summary>Validates the antiforgery token on the current request. Throws if invalid.</summary>
    void ValidateRequest(HttpContext context);

    /// <summary>Returns true if the antiforgery token is valid, false otherwise.</summary>
    bool IsRequestValid(HttpContext context);
}

public sealed class AntiforgeryTokenSet(string cookieToken, string requestToken)
{
    public string CookieToken => cookieToken;
    public string RequestToken => requestToken;
}

public sealed class DefaultAntiforgeryService(AntiforgeryOptions options) : IAntiforgeryService
{
    // In-memory token store: cookieToken → issuedAt
    // For a real distributed deployment, replace with IDistributedCache
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTimeOffset> _tokens = new();

    public AntiforgeryTokenSet GetAndStoreTokens(HttpContext context)
    {
        // Reuse existing cookie token if present and valid
        string? existing = null;
        if (context.Request.Cookies.TryGetValue(options.CookieName, out var cookie) && IsKnownToken(cookie))
            existing = cookie;

        var cookieToken = existing ?? GenerateToken();
        _tokens[cookieToken] = DateTimeOffset.UtcNow;

        // Issue the cookie
        context.Response.Headers["Set-Cookie"] =
            $"{options.CookieName}={cookieToken}; Path=/; HttpOnly; SameSite=Strict";

        // The request token is a HMAC of the cookie token (same-origin binding)
        var requestToken = DeriveRequestToken(cookieToken);
        return new AntiforgeryTokenSet(cookieToken, requestToken);
    }

    public void ValidateRequest(HttpContext context)
    {
        if (!IsRequestValid(context))
            throw new InvalidOperationException("The antiforgery token could not be validated.");
    }

    public bool IsRequestValid(HttpContext context)
    {
        // GET, HEAD, OPTIONS are safe methods — skip validation
        if (context.Request.Method is Http.HttpMethod.GET or Http.HttpMethod.HEAD or Http.HttpMethod.OPTIONS)
            return true;

        context.Request.Cookies.TryGetValue(options.CookieName, out var cookieToken);
        if (string.IsNullOrEmpty(cookieToken) || !IsKnownToken(cookieToken))
            return false;

        // Check header first, then form body (only when Content-Type is form-encoded).
        // Calling ReadForm() on a JSON or binary body would silently return empty and
        // bypass validation — always guard with a Content-Type check first.
        string? requestToken = null;
        if (context.Request.Headers.TryGetValue(options.HeaderName, out var headerVal))
        {
            requestToken = headerVal;
        }
        else if (context.Request.Headers.TryGetValue("Content-Type", out var ct) &&
                 ct.ToString().StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
        {
            var form = context.Request.ReadForm();
            form.Fields.TryGetValue(options.FormFieldName, out requestToken);
        }

        if (string.IsNullOrEmpty(requestToken))
            return false;

        var expected = DeriveRequestToken(cookieToken);
        return CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(requestToken),
            System.Text.Encoding.UTF8.GetBytes(expected));
    }

    private bool IsKnownToken(string token) => _tokens.ContainsKey(token);

    private static string GenerateToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static string DeriveRequestToken(string cookieToken)
    {
        // HMAC-SHA256 of the cookie token with a per-process key
        var key = LazyKey.Value;
        var mac = HMACSHA256.HashData(key, System.Text.Encoding.UTF8.GetBytes(cookieToken));
        return Convert.ToBase64String(mac);
    }

    // Per-process random key — not suitable for multi-instance; replace key material from config for that
    private static readonly Lazy<byte[]> LazyKey = new(() => RandomNumberGenerator.GetBytes(32));
}

// ── Attribute ─────────────────────────────────────────────────────────────────

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class ValidateAntiforgeryAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class IgnoreAntiforgeryAttribute : Attribute { }

// ── Middleware ─────────────────────────────────────────────────────────────────

/// <summary>
/// Validates antiforgery tokens on state-changing requests (POST/PUT/PATCH/DELETE).
/// Must be registered after <c>UseSession()</c> or any cookie-reading middleware.
/// </summary>
public sealed class AntiforgeryMiddleware(IAntiforgeryService antiforgery) : IMiddleware
{
    public async ValueTask InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (context.Request.Method is not (Http.HttpMethod.GET or Http.HttpMethod.HEAD or Http.HttpMethod.OPTIONS))
        {
            if (!antiforgery.IsRequestValid(context))
            {
                context.Response.StatusCode = 400;
                context.Response.WriteText("Invalid or missing antiforgery token.");
                return;
            }
        }

        await next(context);
    }
}
