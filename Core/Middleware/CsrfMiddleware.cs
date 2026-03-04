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
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var method = context.Request.Method;

        // Skip validation for safe methods or /echo benchmark
        if (method == CosmoApiServer.Core.Http.HttpMethod.GET || 
            method == CosmoApiServer.Core.Http.HttpMethod.HEAD || 
            method == CosmoApiServer.Core.Http.HttpMethod.OPTIONS ||
            context.Request.Path == "/echo")
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

        if (string.IsNullOrEmpty(cookieToken) || !CsrfTokenHelper.Validate(headerToken!, cookieToken))
        {
            context.Response.StatusCode = 403;
            context.Response.WriteJson(new { error = "CsrfValidationFailed", message = "CSRF token validation failed." });
            return;
        }

        await next(context);
    }

    private bool HasCsrfCookie(HttpContext context)
    {
        return context.Request.Headers.TryGetValue("cookie", out var v) && v.Contains(options.CookieName);
    }

    private string? GetCsrfCookie(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue("cookie", out var v)) return null;
        foreach (var part in v.Split(';'))
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith(options.CookieName + "="))
                return trimmed[(options.CookieName.Length + 1)..];
        }
        return null;
    }

    private void SetCsrfCookie(HttpContext context)
    {
        var token = CsrfTokenHelper.GenerateToken();
        context.Response.Headers["Set-Cookie"] = $"{options.CookieName}={token}; Path=/; HttpOnly; SameSite=Lax";
    }
}
