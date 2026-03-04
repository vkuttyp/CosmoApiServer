using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;

namespace CosmoApiServer.Core.Http;

public sealed class HttpContext
{
    public HttpRequest Request { get; }
    public HttpResponse Response { get; }
    public IServiceProvider RequestServices { get; set; }

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

    public HttpContext(HttpRequest request, HttpResponse response, IServiceProvider services)
    {
        Request = request;
        Response = response;
        RequestServices = services;
    }
}
