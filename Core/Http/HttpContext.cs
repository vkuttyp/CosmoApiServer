using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;

namespace CosmoApiServer.Core.Http;

public sealed class HttpContext
{
    public HttpRequest Request { get; }
    public HttpResponse Response { get; }
    public IServiceProvider RequestServices { get; set; }

    /// <summary>
    /// Gets or sets a cancellation token that signals when the request is aborted.
    /// </summary>
    public CancellationToken RequestAborted { get; set; }

    /// <summary>
    /// Per-request storage for middleware and framework components.
    /// </summary>
    public Dictionary<object, object?> Items { get; } = new();

    /// <summary>
    /// Whether the current request is a WebSocket upgrade request.
    /// </summary>
    public bool IsWebSocketRequest => WebSocketHelper.IsWebSocketRequest(Request);

    /// <summary>The authenticated user, set by JwtMiddleware. Null if unauthenticated.</summary>
    public ClaimsPrincipal? User { get; set; }

    /// <summary>
    /// Set by ControllerScanner when the action returns IAsyncEnumerable&lt;T&gt;.
    /// The transport writes chunked/stream headers then calls this with a raw Stream
    /// (chunked-encoding wrapper for HTTP/1.1, or a DATA-frame stream for HTTP/2).
    /// </summary>
    internal Func<Stream, Task>? StreamingBodyWriter { get; set; }

    /// <summary>Lifecycle: the transport disposes this after the response is sent.</summary>
    internal IDisposable? _disposeScope;

    public HttpContext(HttpRequest request, HttpResponse response, IServiceProvider services, CancellationToken requestAborted = default)
    {
        Request = request;
        Response = response;
        Response.HttpContext = this;
        RequestServices = services;
        RequestAborted = requestAborted;
    }

    /// <summary>
    /// Accepts a WebSocket upgrade request and returns a high-performance WebSocket instance.
    /// </summary>
    public async Task<CosmoWebSocket> AcceptWebSocketAsync()
    {
        if (!IsWebSocketRequest)
            throw new InvalidOperationException("Not a WebSocket request.");

        var key = Request.Headers["Sec-WebSocket-Key"];
        var responseKey = WebSocketHelper.CreateResponseKey(key);

        Response.StatusCode = 101;
        Response.Headers["Upgrade"] = "websocket";
        Response.Headers["Connection"] = "Upgrade";
        Response.Headers["Sec-WebSocket-Accept"] = responseKey;

        // Signal to the transport that we are switching protocols.
        Items["__WebSocketUpgrade"] = true;

        if (Items.TryGetValue("__RawStream", out var stream) && stream is Stream s)
        {
            return new CosmoWebSocket(s);
        }

        throw new InvalidOperationException("Raw stream not available for WebSocket upgrade.");
    }
}
