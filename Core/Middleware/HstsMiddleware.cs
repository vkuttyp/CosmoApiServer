using CosmoApiServer.Core.Http;

namespace CosmoApiServer.Core.Middleware;

public sealed class HstsOptions
{
    public int MaxAge { get; set; } = 31536000; // 1 year
    public bool IncludeSubDomains { get; set; } = true;
    public bool Preload { get; set; } = false;
}

/// <summary>
/// Middleware to add the Strict-Transport-Security header.
/// </summary>
public sealed class HstsMiddleware(HstsOptions options) : IMiddleware
{
    public async ValueTask InvokeAsync(HttpContext context, RequestDelegate next)
    {
        // HSTS header only applies over HTTPS. 
        // We only set it if the request is secure or if we know we're being proxied over HTTPS.
        if (IsHttps(context))
        {
            var headerValue = $"max-age={options.MaxAge}";
            if (options.IncludeSubDomains) headerValue += "; includeSubDomains";
            if (options.Preload) headerValue += "; preload";
            
            context.Response.Headers["Strict-Transport-Security"] = headerValue;
        }

        await next(context);
    }

    private static bool IsHttps(HttpContext context)
    {
        if (context.Items.TryGetValue("__IsHttps", out var isHttps) && isHttps is true)
            return true;
            
        if (context.Request.Headers.TryGetValue("x-forwarded-proto", out var proto) && proto == "https")
            return true;

        return false;
    }
}
