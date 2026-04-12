using CosmoApiServer.Core.Http;

namespace CosmoApiServer.Core.Middleware;

public sealed class ViteDevProxyOptions
{
    /// <summary>The Vite/Nuxt dev server base URL (e.g. "http://127.0.0.1:3000").</summary>
    public string DevServerUrl { get; set; } = "http://127.0.0.1:3000";

    /// <summary>
    /// Request path prefixes forwarded to the dev server.
    /// Covers Vite virtual modules, file-system imports, and Nuxt internals.
    /// </summary>
    public string[] ProxiedPrefixes { get; set; } =
    [
        "/@vite",
        "/@fs",
        "/@id",
        "/_nuxt",
        "/__nuxt",
        "/node_modules/.vite",
        "/__vite_ping"
    ];
}

/// <summary>
/// Forwards Vite and Nuxt dev-server paths (module scripts, virtual modules, Nuxt internals)
/// to the running dev server, enabling a single-port integrated development setup where the
/// Cosmo backend and the Vite/Nuxt frontend share one port.
///
/// Register this before <c>UseViteFrontend</c> so dev-server assets are served directly
/// rather than being rendered as SPA shells.
///
/// For HMR WebSocket proxying, also register <see cref="ReverseProxyMiddleware"/> at the
/// root path with the dev server as the destination.
/// </summary>
public sealed class ViteDevProxyMiddleware : IMiddleware
{
    private static readonly HttpClient _client = new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false
    })
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private readonly string _devServerUrl;
    private readonly string[] _proxiedPrefixes;

    // Hop-by-hop headers must not be forwarded to or from the upstream.
    private static readonly HashSet<string> HopByHop = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection", "Keep-Alive", "Transfer-Encoding", "TE",
        "Trailer", "Upgrade", "Proxy-Authorization", "Proxy-Authenticate"
    };

    public ViteDevProxyMiddleware(ViteDevProxyOptions options)
    {
        _devServerUrl = options.DevServerUrl.TrimEnd('/');
        _proxiedPrefixes = options.ProxiedPrefixes
            .Select(static p => p.StartsWith('/') ? p : "/" + p)
            .ToArray();
    }

    public async ValueTask InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (!ShouldProxy(context.Request.Path))
        {
            await next(context);
            return;
        }

        // HMR WebSocket connections require a lower-level stream relay that goes beyond
        // the middleware HTTP pipeline. Register ReverseProxyMiddleware for full WS support.
        if (IsWebSocketUpgrade(context.Request))
        {
            context.Response.StatusCode = 501;
            context.Response.WriteText(
                "HMR WebSocket proxying requires ReverseProxyMiddleware. " +
                "Add builder.UseReverseProxy() or run the frontend on a separate port in dev.");
            return;
        }

        var qs = context.Request.QueryString;
        var targetUrl = _devServerUrl + context.Request.Path +
                        (string.IsNullOrEmpty(qs) ? "" : "?" + qs.TrimStart('?'));

        using var upstream = new HttpRequestMessage(
            new System.Net.Http.HttpMethod(context.Request.Method.ToString()),
            targetUrl);

        // Forward request headers, excluding hop-by-hop.
        foreach (var (name, value) in context.Request.Headers)
        {
            if (!HopByHop.Contains(name))
                upstream.Headers.TryAddWithoutValidation(name, value);
        }

        // Forward the request body.
        if (context.Request.BodyStream != Stream.Null)
            upstream.Content = new StreamContent(context.Request.BodyStream);
        else if (context.Request.Body.Length > 0)
            upstream.Content = new ByteArrayContent(context.Request.Body);

        try
        {
            using var response = await _client.SendAsync(
                upstream, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);

            context.Response.StatusCode = (int)response.StatusCode;

            // Copy response headers, excluding hop-by-hop.
            foreach (var (name, values) in response.Headers)
                if (!HopByHop.Contains(name))
                    context.Response.Headers[name] = string.Join(", ", values);

            foreach (var (name, values) in response.Content.Headers)
                if (!HopByHop.Contains(name))
                    context.Response.Headers[name] = string.Join(", ", values);

            // Remove Content-Length so the response writer switches to chunked mode,
            // allowing us to stream without knowing the total size up front.
            context.Response.Headers.Remove("Content-Length");

            // Stream the proxy response body in chunks.
            using var contentStream = await response.Content.ReadAsStreamAsync(context.RequestAborted);
            var buffer = new byte[8192];
            int read;
            while ((read = await contentStream.ReadAsync(buffer, context.RequestAborted)) > 0)
                context.Response.Write(buffer.AsSpan(0, read));

            context.Response.End();
        }
        catch (HttpRequestException)
        {
            context.Response.StatusCode = 502;
            context.Response.WriteText($"Vite dev server at '{_devServerUrl}' is not reachable. Is it running?");
        }
        catch (TaskCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Client disconnected — not an error.
        }
    }

    private bool ShouldProxy(string path)
    {
        foreach (var prefix in _proxiedPrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool IsWebSocketUpgrade(HttpRequest req) =>
        req.Headers.TryGetValue("Upgrade", out var upgrade) &&
        upgrade.Equals("websocket", StringComparison.OrdinalIgnoreCase);
}
