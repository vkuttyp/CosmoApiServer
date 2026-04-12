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
    /// <summary>
    /// Which forwarded headers to process. Defaults to <see cref="ForwardedHeaders.None"/> —
    /// you must explicitly opt in to avoid blindly trusting attacker-controlled headers.
    /// </summary>
    public ForwardedHeaders ForwardedHeaders { get; set; } = ForwardedHeaders.None;
    /// <summary>Maximum number of entries to read from X-Forwarded-For (0 = unlimited).</summary>
    public int ForwardLimit { get; set; } = 1;
    /// <summary>
    /// Trusted proxy IPs. When non-empty, forwarded headers are only processed when the
    /// direct remote IP is in this list (or <see cref="KnownNetworks"/>).
    /// </summary>
    public IList<string> KnownProxies { get; } = [];
    /// <summary>
    /// Trusted proxy networks in CIDR notation (e.g. "10.0.0.0/8"). Parsed at first use.
    /// </summary>
    public IList<string> KnownNetworks { get; } = [];
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

        // Only process forwarded headers when the direct connection comes from a
        // trusted proxy. If KnownProxies and KnownNetworks are both empty we skip
        // all processing — callers must explicitly configure trusted proxies.
        if (!IsTrustedProxy(context.Items["RemoteIpAddress"] as string))
        {
            await next(context);
            return;
        }

        if (options.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedFor) &&
            req.Headers.TryGetValue("X-Forwarded-For", out var xff) &&
            !string.IsNullOrEmpty(xff))
        {
            // Take the rightmost address within the ForwardLimit window —
            // that is the entry written by the closest trusted proxy.
            var addresses = xff.Split(',', StringSplitOptions.RemoveEmptyEntries);
            var limit = options.ForwardLimit > 0 ? Math.Min(options.ForwardLimit, addresses.Length) : addresses.Length;
            var clientIp = addresses[^limit].Trim();
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

    private bool IsTrustedProxy(string? remoteIp)
    {
        // No trust list configured → reject all (safe default).
        if (options.KnownProxies.Count == 0 && options.KnownNetworks.Count == 0)
            return false;

        if (string.IsNullOrEmpty(remoteIp))
            return false;

        // Exact IP match
        if (options.KnownProxies.Contains(remoteIp, StringComparer.OrdinalIgnoreCase))
            return true;

        // CIDR network match
        if (System.Net.IPAddress.TryParse(remoteIp, out var ip))
        {
            foreach (var cidr in options.KnownNetworks)
            {
                if (IsInCidr(ip, cidr))
                    return true;
            }
        }

        return false;
    }

    private static bool IsInCidr(System.Net.IPAddress ip, string cidr)
    {
        var parts = cidr.Split('/');
        if (parts.Length != 2) return false;
        if (!System.Net.IPAddress.TryParse(parts[0], out var network)) return false;
        if (!int.TryParse(parts[1], out var prefixLen)) return false;

        var ipBytes = ip.GetAddressBytes();
        var netBytes = network.GetAddressBytes();
        if (ipBytes.Length != netBytes.Length) return false;

        int fullBytes = prefixLen / 8;
        int remainBits = prefixLen % 8;

        for (int i = 0; i < fullBytes; i++)
            if (ipBytes[i] != netBytes[i]) return false;

        if (remainBits > 0 && fullBytes < ipBytes.Length)
        {
            int mask = 0xFF << (8 - remainBits);
            if ((ipBytes[fullBytes] & mask) != (netBytes[fullBytes] & mask)) return false;
        }

        return true;
    }
}
