using System.Net.Sockets;
using System.Net.WebSockets;
using CosmoApiServer.Core.Http;
using CosmoApiServer.Core.Transport;

namespace CosmoApiServer.Core.Middleware;

public sealed class ProxyRoute
{
    /// <summary>Forward requests whose path starts with this prefix.</summary>
    public string PathPrefix { get; set; } = "/";

    /// <summary>Upstream base URL (e.g. "http://127.0.0.1:3000").</summary>
    public string Destination { get; set; } = "";

    /// <summary>Paths excluded from forwarding even when they match <see cref="PathPrefix"/>.</summary>
    public string[] ExcludedPrefixes { get; set; } = [];

    /// <summary>
    /// When true, the original <c>Host</c> header is forwarded as-is.
    /// When false (default), <c>Host</c> is rewritten to the upstream host.
    /// </summary>
    public bool PreserveHost { get; set; } = false;

    /// <summary>
    /// When true, uses the legacy HttpClient-based forwarder instead of the pipeline-based forwarder.
    /// Default is false (pipeline-based).
    /// </summary>
    public bool UseLegacyHttpClient { get; set; } = false;

    /// <summary>
    /// If set, this route only matches when the request carries the named
    /// header (any value). Lets a single port host BOTH a public-facing
    /// reverse-proxy and a backend API for an SSR layer that calls back into
    /// itself: gate the forward route on <c>X-Forwarded-For</c> (cosmoproxy
    /// adds it; loopback <c>$fetch</c>/HttpClient calls don't), so externally-
    /// originated requests get forwarded to the SSR upstream while the SSR
    /// runtime's own backend calls fall through to local endpoint dispatch.
    /// Without this, the SSR layer's <c>$fetch(baseURL=http://127.0.0.1:port)</c>
    /// loops back through the reverse-proxy infinitely.
    /// </summary>
    public string? OnlyIfHeader { get; set; }
}

public sealed class ReverseProxyOptions
{
    public List<ProxyRoute> Routes { get; set; } = [];
}

/// <summary>
/// Routes-based reverse proxy supporting both HTTP and WebSocket connections.
///
/// Uses pipeline-based zero-copy forwarding by default. The request is written directly
/// to the upstream socket via PipeWriter, and the response body is spliced from the
/// upstream PipeReader to the downstream response — no intermediate buffer copies.
///
/// Typical use case — Nuxt SSR production architecture:
/// <code>
/// builder.UseReverseProxy(opts =>
///     opts.Routes.Add(new ProxyRoute
///     {
///         PathPrefix        = "/",
///         Destination       = "http://127.0.0.1:3000",
///         ExcludedPrefixes  = ["/api", "/health"]
///     }));
/// </code>
///
/// For integrated single-port dev mode, use <see cref="ViteDevProxyMiddleware"/> for
/// Vite-specific paths and this middleware for HMR WebSocket proxying.
/// </summary>
public sealed class ReverseProxyMiddleware : IMiddleware
{
    private static readonly PipelineHttpForwarder _pipelineForwarder = new();

    // Legacy fallback for routes that opt in to HttpClient
    private static readonly HttpClient _httpClient = new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseCookies        = false
    })
    {
        Timeout = TimeSpan.FromSeconds(60)
    };

    private static readonly HashSet<string> HopByHop = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection", "Keep-Alive", "Transfer-Encoding", "TE",
        "Trailer", "Upgrade", "Proxy-Authorization", "Proxy-Authenticate"
    };

    private readonly List<ProxyRoute> _routes;

    public ReverseProxyMiddleware(ReverseProxyOptions options) =>
        _routes = options.Routes;

    public async ValueTask InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var route = FindRoute(context);
        if (route is null)
        {
            await next(context);
            return;
        }

        if (IsWebSocketUpgrade(context.Request))
        {
            await ProxyWebSocketAsync(context, route);
            return;
        }

        if (route.UseLegacyHttpClient)
        {
            await ProxyHttpLegacyAsync(context, route);
            return;
        }

        await ProxyHttpPipelineAsync(context, route);
    }

    // ── Pipeline-based HTTP proxy (zero-copy) ─────────────────────────────────

    private static async Task ProxyHttpPipelineAsync(HttpContext context, ProxyRoute route)
    {
        var destination = route.Destination.TrimEnd('/');
        var uri = new Uri(destination);
        var qs = context.Request.QueryString;
        var pathAndQuery = context.Request.Path +
                           (string.IsNullOrEmpty(qs) ? "" : "?" + qs.TrimStart('?'));

        Dictionary<string, string>? extraHeaders = null;
        if (route.PreserveHost && !string.IsNullOrEmpty(context.Request.Host))
        {
            extraHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Host"] = context.Request.Host
            };
        }

        try
        {
            await _pipelineForwarder.ForwardAsync(
                context,
                uri.Scheme,
                uri.Host,
                uri.Port,
                pathAndQuery,
                extraHeaders,
                null,
                context.RequestAborted);
        }
        catch (IOException)
        {
            if (!context.Response.IsStarted)
            {
                context.Response.StatusCode = 502;
                context.Response.WriteText($"Upstream '{route.Destination}' is not reachable.");
            }
        }
        catch (SocketException)
        {
            if (!context.Response.IsStarted)
            {
                context.Response.StatusCode = 502;
                context.Response.WriteText($"Upstream '{route.Destination}' is not reachable.");
            }
        }
        catch (TaskCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Client disconnected.
        }
    }

    // ── Legacy HttpClient-based proxy ─────────────────────────────────────────

    private static async Task ProxyHttpLegacyAsync(HttpContext context, ProxyRoute route)
    {
        var destination = route.Destination.TrimEnd('/');
        var qs          = context.Request.QueryString;
        var targetUrl   = destination + context.Request.Path +
                          (string.IsNullOrEmpty(qs) ? "" : "?" + qs.TrimStart('?'));

        using var upstream = new HttpRequestMessage(
            new System.Net.Http.HttpMethod(context.Request.Method.ToString()),
            targetUrl);

        foreach (var (name, value) in context.Request.Headers)
        {
            if (HopByHop.Contains(name)) continue;
            if (!route.PreserveHost && name.Equals("Host", StringComparison.OrdinalIgnoreCase)) continue;
            upstream.Headers.TryAddWithoutValidation(name, value);
        }

        if (!route.PreserveHost)
        {
            var uri = new Uri(destination);
            upstream.Headers.Host = uri.Authority;
        }

        if (context.Request.BodyStream != Stream.Null)
            upstream.Content = new StreamContent(context.Request.BodyStream);
        else if (context.Request.Body.Length > 0)
            upstream.Content = new ByteArrayContent(context.Request.Body);

        try
        {
            using var response = await _httpClient.SendAsync(
                upstream, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);

            context.Response.StatusCode = (int)response.StatusCode;

            foreach (var (name, values) in response.Headers)
            {
                if (HopByHop.Contains(name)) continue;
                if (name.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var v in values)
                        context.Response.SetCookieHeaders.Add(v);
                    continue;
                }
                context.Response.Headers[name] = string.Join(", ", values);
            }

            foreach (var (name, values) in response.Content.Headers)
            {
                if (HopByHop.Contains(name)) continue;
                if (name.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var v in values)
                        context.Response.SetCookieHeaders.Add(v);
                    continue;
                }
                context.Response.Headers[name] = string.Join(", ", values);
            }

            context.Response.Headers.Remove("Content-Length");

            using var body = await response.Content.ReadAsStreamAsync(context.RequestAborted);
            var buffer = new byte[8192];
            int read;
            while ((read = await body.ReadAsync(buffer, context.RequestAborted)) > 0)
                context.Response.Write(buffer.AsSpan(0, read));

            context.Response.End();
        }
        catch (HttpRequestException)
        {
            context.Response.StatusCode = 502;
            context.Response.WriteText($"Upstream '{route.Destination}' is not reachable.");
        }
        catch (TaskCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Client disconnected.
        }
    }

    // ── WebSocket proxy ───────────────────────────────────────────────────────

    private static async Task ProxyWebSocketAsync(HttpContext context, ProxyRoute route)
    {
        // Build the upstream WebSocket URI (ws:// or wss://).
        var destination  = route.Destination.TrimEnd('/');
        var wsDestination = destination
            .Replace("http://",  "ws://",  StringComparison.OrdinalIgnoreCase)
            .Replace("https://", "wss://", StringComparison.OrdinalIgnoreCase);

        var qs        = context.Request.QueryString;
        var targetUri = new Uri(wsDestination + context.Request.Path +
                                (string.IsNullOrEmpty(qs) ? "" : "?" + qs.TrimStart('?')));

        // Attempt to accept the inbound WebSocket from the context.
        // The framework stores the accepted WebSocket in Items["WebSocket"] after upgrade.
        if (!context.Items.TryGetValue("WebSocket", out var wsObj) || wsObj is not WebSocket inbound)
        {
            // Framework did not perform the upgrade — respond with 501 so the client knows.
            context.Response.StatusCode = 501;
            context.Response.WriteText(
                "WebSocket proxying requires transport-level upgrade support. " +
                "Ensure the framework has completed the WebSocket handshake before reaching " +
                "ReverseProxyMiddleware (context.Items[\"WebSocket\"] must be set).");
            return;
        }

        using var outbound = new ClientWebSocket();

        // Forward request headers to the upstream WebSocket server.
        foreach (var (name, value) in context.Request.Headers)
        {
            if (HopByHop.Contains(name) ||
                name.Equals("Host",       StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Upgrade",    StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Connection", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Sec-WebSocket-", StringComparison.OrdinalIgnoreCase))
                continue;

            outbound.Options.SetRequestHeader(name, value);
        }

        try
        {
            await outbound.ConnectAsync(targetUri, context.RequestAborted);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ReverseProxy] WebSocket connect to '{targetUri}' failed: {ex.Message}");
            return;
        }

        // Bidirectional relay until either side closes or the request is cancelled.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);

        await Task.WhenAll(
            RelayAsync(inbound,  outbound, cts.Token, cts),
            RelayAsync(outbound, inbound,  cts.Token, cts));
    }

    private static async Task RelayAsync(
        WebSocket source,
        WebSocket target,
        CancellationToken ct,
        CancellationTokenSource cts)
    {
        var buffer = new byte[4096];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var result = await source.ReceiveAsync(buffer, ct);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    if (target.State == WebSocketState.Open)
                        await target.CloseAsync(
                            result.CloseStatus ?? WebSocketCloseStatus.NormalClosure,
                            result.CloseStatusDescription,
                            CancellationToken.None);
                    break;
                }

                await target.SendAsync(
                    buffer.AsMemory(0, result.Count),
                    result.MessageType,
                    result.EndOfMessage,
                    ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        finally
        {
            // Signal the other relay task to stop.
            cts.Cancel();
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private ProxyRoute? FindRoute(HttpContext context)
    {
        var path = context.Request.Path;
        foreach (var route in _routes)
        {
            if (!path.StartsWith(route.PathPrefix, StringComparison.OrdinalIgnoreCase))
                continue;

            bool excluded = false;
            foreach (var ex in route.ExcludedPrefixes)
            {
                if (path.StartsWith(ex, StringComparison.OrdinalIgnoreCase))
                {
                    excluded = true;
                    break;
                }
            }

            if (excluded) continue;

            if (!string.IsNullOrEmpty(route.OnlyIfHeader))
            {
                // Require a non-empty value, not just key presence. SSR layers
                // that loop back into the same port often clear the gating
                // header by sending it as an empty string (h3's proxyRequest
                // does this when we pass `{ 'x-forwarded-for': '' }` to break
                // the recursion). ContainsKey alone would still match.
                if (!context.Request.Headers.TryGetValue(route.OnlyIfHeader, out var hv)
                    || string.IsNullOrEmpty(hv))
                {
                    continue;
                }
            }

            return route;
        }
        return null;
    }

    private static bool IsWebSocketUpgrade(HttpRequest req) =>
        req.Headers.TryGetValue("Upgrade", out var upgrade) &&
        upgrade.Equals("websocket", StringComparison.OrdinalIgnoreCase);
}
