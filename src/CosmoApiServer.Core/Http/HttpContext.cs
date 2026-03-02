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
    /// When set by ControllerScanner, HttpChannelHandler will write a chunked response
    /// instead of the buffered DefaultFullHttpResponse. The argument is IChannelHandlerContext (boxed).
    /// </summary>
    internal Func<object, Task>? ChunkedBodyWriter { get; set; }

    public HttpContext(HttpRequest request, HttpResponse response, IServiceProvider services)
    {
        Request = request;
        Response = response;
        RequestServices = services;
    }
}
