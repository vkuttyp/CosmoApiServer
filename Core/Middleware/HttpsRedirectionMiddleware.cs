using CosmoApiServer.Core.Http;

namespace CosmoApiServer.Core.Middleware;

public sealed class HttpsRedirectionOptions
{
    public int? HttpsPort { get; set; } = 443;
    public int StatusCode { get; set; } = 307; // Temporary Redirect
}

/// <summary>
/// Middleware to redirect HTTP requests to HTTPS.
/// </summary>
public sealed class HttpsRedirectionMiddleware(HttpsRedirectionOptions options) : IMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        // Check if the request is already HTTPS. 
        // In this framework, we can check a custom item set by the transport or a header.
        if (IsHttps(context))
        {
            await next(context);
            return;
        }

        var request = context.Request;
        var host = request.Headers.TryGetValue("host", out var h) ? h : "localhost";
        
        // Remove existing port if present and append HTTPS port
        if (host.Contains(':'))
        {
            host = host[..host.IndexOf(':')];
        }

        if (options.HttpsPort.HasValue && options.HttpsPort != 443)
        {
            host += ":" + options.HttpsPort.Value;
        }

        var destination = $"https://{host}{request.Path}{request.QueryString}";
        
        context.Response.StatusCode = options.StatusCode;
        context.Response.Headers["Location"] = destination;
    }

    private static bool IsHttps(HttpContext context)
    {
        // The transport layer should set this in context.Items when using SSL
        if (context.Items.TryGetValue("__IsHttps", out var isHttps) && isHttps is true)
            return true;
            
        // Also check standard proxy header
        if (context.Request.Headers.TryGetValue("x-forwarded-proto", out var proto) && proto == "https")
            return true;

        return false;
    }
}
