using CosmoApiServer.Core.Auth;
using CosmoApiServer.Core.Http;

namespace CosmoApiServer.Core.Middleware;

public sealed class CsrfOptions
{
    public string CookieName { get; set; } = "XSRF-TOKEN";
    public string HeaderName { get; set; } = "X-XSRF-TOKEN";
}

/// <summary>
/// Minimalist CSRF middleware using Double Submit Cookie pattern.
/// For performance, it only validates for state-changing requests (POST, PUT, DELETE, PATCH).
/// </summary>
public sealed class CsrfMiddleware(CsrfOptions options) : IMiddleware
{
    public async ValueTask InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var method = context.Request.Method;

        // Skip validation for safe methods only
        if (method == CosmoApiServer.Core.Http.HttpMethod.GET ||
            method == CosmoApiServer.Core.Http.HttpMethod.HEAD ||
            method == CosmoApiServer.Core.Http.HttpMethod.OPTIONS)
        {
            // Ensure CSRF cookie is present
            if (!HasCsrfCookie(context))
            {
                SetCsrfCookie(context);
            }
            await next(context);
            return;
        }

        // Validate token
        var cookieToken = GetCsrfCookie(context);
        var headerToken = context.Request.Headers.TryGetValue(options.HeaderName, out var v) ? v : null;

        if (string.IsNullOrEmpty(cookieToken) || string.IsNullOrEmpty(headerToken) || !CsrfTokenHelper.Validate(headerToken, cookieToken))
        {
            context.Response.StatusCode = 403;
            context.Response.WriteJson(new { error = "CsrfValidationFailed", message = "CSRF token validation failed." });
            return;
        }

        await next(context);
    }

    private bool HasCsrfCookie(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue("cookie", out var v)) return false;
        // Parse cookie pairs properly to avoid substring false positives
        // (e.g., "MY-XSRF-TOKEN" matching "XSRF-TOKEN")
        foreach (var part in v.Split(';'))
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith(options.CookieName + "=", StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private string? GetCsrfCookie(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue("cookie", out var v)) return null;
        foreach (var part in v.Split(';'))
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith(options.CookieName + "=", StringComparison.Ordinal))
                return trimmed[(options.CookieName.Length + 1)..];
        }
        return null;
    }

    private void SetCsrfCookie(HttpContext context)
    {
        var token = CsrfTokenHelper.GenerateToken();
        // NOTE: Do NOT set HttpOnly — the Double Submit Cookie pattern requires JavaScript
        // to read this cookie and send it back as a header. HttpOnly would prevent that.
        context.Response.Headers["Set-Cookie"] = $"{options.CookieName}={token}; Path=/; SameSite=Lax; Secure";
    }
}
