using CosmoApiServer.Core.Http;

namespace CosmoApiServer.Core.Middleware;

public sealed class CorsOptions
{
    public string[] AllowedOrigins { get; set; } = ["*"];
    public string[] AllowedMethods { get; set; } = ["GET", "POST", "PUT", "DELETE", "PATCH", "OPTIONS"];
    public string[] AllowedHeaders { get; set; } = ["Content-Type", "Authorization"];

    public void AllowAnyOrigin() => AllowedOrigins = ["*"];
    public void AllowAnyMethod() => AllowedMethods = ["*"];
    public void AllowAnyHeader() => AllowedHeaders = ["*"];
}

public sealed class CorsMiddleware : IMiddleware
{
    private readonly CorsOptions _options;
    private readonly string _allowedMethodsHeader;  // pre-computed once
    private readonly string _allowedHeadersHeader;  // pre-computed once
    private readonly bool _allowAll;

    public CorsMiddleware(CorsOptions options)
    {
        _options = options;
        _allowedMethodsHeader = string.Join(", ", options.AllowedMethods);
        _allowedHeadersHeader = string.Join(", ", options.AllowedHeaders);
        _allowAll = Array.IndexOf(options.AllowedOrigins, "*") >= 0;
    }

    public async ValueTask InvokeAsync(HttpContext context, RequestDelegate next)
    {
        // Requests with no Origin header are not cross-origin browser requests.
        // Do not synthesise a wildcard — just skip CORS header injection entirely.
        if (!context.Request.Headers.TryGetValue("Origin", out var o) || string.IsNullOrEmpty(o))
        {
            await next(context);
            return;
        }

        string origin = o!;

        // Prevent a spoofed "Origin: *" from matching the internal _allowAll sentinel.
        bool allowed = _allowAll && origin != "*"
                    || Array.IndexOf(_options.AllowedOrigins, origin) >= 0;

        // Handle pre-flight OPTIONS request
        if (context.Request.Method == Http.HttpMethod.OPTIONS)
        {
            if (!allowed)
            {
                // Deny the preflight — return 403 so the browser knows the origin is blocked
                context.Response.StatusCode = 403;
                return;
            }
            context.Response.Headers["Access-Control-Allow-Origin"]  = origin;
            context.Response.Headers["Access-Control-Allow-Methods"] = _allowedMethodsHeader;
            context.Response.Headers["Access-Control-Allow-Headers"] = _allowedHeadersHeader;
            context.Response.Headers["Access-Control-Max-Age"]       = "3600";
            context.Response.StatusCode = 204;
            return;
        }

        if (allowed)
        {
            // Only the Allow-Origin header is meaningful on non-preflight responses.
            // Allow-Methods and Allow-Headers are preflight-only per the CORS spec.
            context.Response.Headers["Access-Control-Allow-Origin"] = origin;
        }

        await next(context);
    }
}
