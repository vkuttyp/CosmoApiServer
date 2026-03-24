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
    public async ValueTask InvokeAsync(HttpContext context, RequestDelegate next)
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

        // Validate host to prevent header injection / open redirect attacks.
        // Only allow alphanumeric, hyphens, dots, and brackets (for IPv6).
        if (!IsValidHost(host))
        {
            context.Response.StatusCode = 400;
            context.Response.WriteText("Bad Request");
            return;
        }

        if (options.HttpsPort.HasValue && options.HttpsPort != 443)
        {
            host += ":" + options.HttpsPort.Value;
        }

        var qs = !string.IsNullOrEmpty(request.QueryString) ? request.QueryString : string.Empty;
        // Prepend '?' if QueryString doesn't already have it
        if (qs.Length > 0 && qs[0] != '?') qs = "?" + qs;
        var destination = $"https://{host}{request.Path}{qs}";
        
        context.Response.StatusCode = options.StatusCode;
        context.Response.Headers["Location"] = destination;
    }

    private static bool IsValidHost(string host)
    {
        if (string.IsNullOrEmpty(host) || host.Length > 253) return false;
        foreach (var c in host)
        {
            if (!char.IsLetterOrDigit(c) && c != '-' && c != '.' && c != '[' && c != ']')
                return false;
        }
        return true;
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
