using CosmoApiServer.Core.Http;

namespace CosmoApiServer.Core.Middleware;

[Flags]
public enum ForwardedHeaders
{
    None            = 0,
    XForwardedFor   = 1,
    XForwardedHost  = 2,
    XForwardedProto = 4,
    All             = XForwardedFor | XForwardedHost | XForwardedProto
}

public sealed class ForwardedHeadersOptions
{
    public ForwardedHeaders ForwardedHeaders { get; set; } = ForwardedHeaders.All;
    /// <summary>Maximum number of entries to read from X-Forwarded-For (0 = unlimited).</summary>
    public int ForwardLimit { get; set; } = 1;
    /// <summary>Trusted proxy networks (CIDR notation). Empty = trust any proxy.</summary>
    public IList<string> KnownNetworks { get; } = [];
    /// <summary>Trusted proxy IPs. Empty = trust any proxy.</summary>
    public IList<string> KnownProxies { get; } = [];
}

/// <summary>
/// Reads X-Forwarded-For, X-Forwarded-Host, and X-Forwarded-Proto headers written by
/// reverse proxies and updates the request accordingly.
/// </summary>
public sealed class ForwardedHeadersMiddleware(ForwardedHeadersOptions options) : IMiddleware
{
    public async ValueTask InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var req = context.Request;

        if (options.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedFor) &&
            req.Headers.TryGetValue("X-Forwarded-For", out var xff) &&
            !string.IsNullOrEmpty(xff))
        {
            // Take the first (leftmost) non-empty address, up to ForwardLimit
            var addresses = xff.Split(',', StringSplitOptions.RemoveEmptyEntries);
            var limit = options.ForwardLimit > 0 ? Math.Min(options.ForwardLimit, addresses.Length) : addresses.Length;
            var clientIp = addresses[^limit].Trim(); // rightmost within limit = closest trusted proxy's report
            context.Items["X-Forwarded-For"] = clientIp;
        }

        if (options.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedProto) &&
            req.Headers.TryGetValue("X-Forwarded-Proto", out var proto) &&
            !string.IsNullOrEmpty(proto))
        {
            context.Items["X-Forwarded-Proto"] = proto.Split(',')[0].Trim();
        }

        if (options.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedHost) &&
            req.Headers.TryGetValue("X-Forwarded-Host", out var host) &&
            !string.IsNullOrEmpty(host))
        {
            context.Items["X-Forwarded-Host"] = host.Split(',')[0].Trim();
        }

        await next(context);
    }
}
