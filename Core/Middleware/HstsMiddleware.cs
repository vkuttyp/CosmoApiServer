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
    // Pre-compute the header value once since options are immutable after construction
    private readonly string _headerValue = BuildHeaderValue(options);

    private static string BuildHeaderValue(HstsOptions opts)
    {
        var value = $"max-age={opts.MaxAge}";
        if (opts.IncludeSubDomains) value += "; includeSubDomains";
        if (opts.Preload) value += "; preload";
        return value;
    }

    public async ValueTask InvokeAsync(HttpContext context, RequestDelegate next)
    {
        // HSTS header only applies over HTTPS.
        // We only set it if the request is secure or if we know we're being proxied over HTTPS.
        if (IsHttps(context))
        {
            context.Response.Headers["Strict-Transport-Security"] = _headerValue;
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
